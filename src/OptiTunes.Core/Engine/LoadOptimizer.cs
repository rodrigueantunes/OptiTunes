using System.Diagnostics;
using OptiTunes.Core.Models;

namespace OptiTunes.Core.Engine;

/// <summary>
/// Moteur de placement 3D par points extrêmes.
/// Chargement de l'avant (X = 0, cabine) vers la porte arrière (X = Lc), arrêts les plus lointains d'abord.
/// Chaque placement respecte toutes les contraintes obligatoires ; plusieurs ordres de tri sont testés
/// et le meilleur plan est conservé. Le plan final est ensuite contrôlé par <see cref="Validation.PlanValidator"/>.
/// </summary>
public sealed class LoadOptimizer
{
    /// <summary>Façon de remplir : parois (largeur d'abord), colonnes (gerbe d'abord), rangées (sens de pose par ordre).</summary>
    private enum PackMode
    {
        Walls,
        Columns,
        Rows
    }

    private sealed record Strategy(string Name, Func<IEnumerable<PhysicalUnit>, IOrderedEnumerable<PhysicalUnit>> Sort);

    private static readonly Strategy[] Strategies =
    [
        new("Emprise au sol décroissante", s => Base(s)
            .ThenByDescending(u => FootprintMax(u)).ThenByDescending(u => u.Volume).ThenByDescending(u => u.Weight)),
        new("Volume décroissant", s => Base(s)
            .ThenByDescending(u => u.Volume).ThenByDescending(u => u.Weight)),
        new("Poids décroissant", s => Base(s)
            .ThenByDescending(u => u.Weight).ThenByDescending(u => u.Volume)),
        new("Plus grande dimension d'abord", s => Base(s)
            .ThenByDescending(u => Math.Max(u.A, Math.Max(u.B, u.C))).ThenByDescending(u => u.Volume)),
        new("Non gerbables d'abord", s => Base(s)
            .ThenBy(u => u.CanReceive).ThenByDescending(u => FootprintMax(u)).ThenByDescending(u => u.Weight)),
        new("Groupé par ordre", s => Base(s)
            .ThenByDescending(u => u.Order.Quantity * u.Volume).ThenBy(u => u.Order.Id).ThenBy(u => u.Index))
    ];

    /// <summary>Plan recommandé : la meilleure des solutions de <see cref="OptimizeSolutions"/>.</summary>
    public LoadPlan Optimize(Groupage groupage, PackingOptions? options = null) =>
        OptimizeSolutions(groupage, options)[0];

    /// <summary>
    /// Toutes les solutions, de la meilleure à la moins bonne, sans doublon :
    /// « meilleure par camion » (chaque camion prend la meilleure combinaison tri × mode) et une solution par
    /// combinaison appliquée à tous les camions. La première est marquée recommandée.
    /// </summary>
    public IReadOnlyList<LoadPlan> OptimizeSolutions(Groupage groupage, PackingOptions? options = null)
    {
        var opts = options ?? new PackingOptions();
        var greedy = Build(groupage, opts, "Meilleure par camion", (v, units, log) => PackBest(v, units, opts, log));
        if (!groupage.IsComputable || !opts.MultiStrategy)
        {
            greedy.IsRecommended = true;
            return [greedy];
        }

        var runs = AllRuns().ToArray();
        var alternatives = new LoadPlan[runs.Length];
        Parallel.For(0, runs.Length, i =>
        {
            var (strategy, mode) = runs[i];
            var name = RunName(strategy, mode);
            alternatives[i] = Build(groupage, opts, name, (v, units, log) =>
            {
                var r = Pack(v, strategy.Sort(units).ToList(), opts, mode);
                r.Strategy = name;
                log.Add(new StrategyOutcome(name, r.Placed.Count, r.Placed.Sum(p => p.Unit.ItemCount),
                    r.Placed.Sum(p => p.Unit.Volume) / 1e9, r.MaxX / 1000, CompleteOrders(r)));
                return r;
            });
        });

        var solutions = new List<LoadPlan>();
        var signatures = new HashSet<string>();
        foreach (var plan in alternatives.Prepend(greedy).OrderBy(Rank))
        {
            if (signatures.Add(Signature(plan)))
            {
                solutions.Add(plan);
            }
        }

        solutions[0].IsRecommended = true;
        return solutions;
    }

    /// <summary>Clé de tri : reliquat, nombre de camions, métrage réel total, ordres scindés entre camions, ordres incomplets.</summary>
    private static (int, int, double, int, int) Rank(LoadPlan p) =>
        (p.Metrics.ItemsRemaining, p.Loads.Count,
         Math.Round(p.Metrics.LinearMetersReal, 1),
         p.Groupage.Orders.Count(o => p.VehiclesOf(o).Count > 1),
         p.Groupage.Orders.Count(o => p.RemainingItems(o) > 0));

    private static string Signature(LoadPlan p) =>
        string.Join("|", p.Placements.OrderBy(x => x.Unit.Id)
            .Select(x => $"{x.Unit.Id}:{x.VehicleNumber}:{x.X:0}:{x.Y:0}:{x.Z:0}:{x.Orientation}"));

    private static IEnumerable<(Strategy St, PackMode Mode)> AllRuns() =>
        Strategies.SelectMany(st => new[] { (st, PackMode.Walls), (st, PackMode.Columns), (st, PackMode.Rows) });

    private static string RunName(Strategy strategy, PackMode mode) => strategy.Name + mode switch
    {
        PackMode.Columns => " · colonnes",
        PackMode.Rows => " · rangées",
        _ => " · parois"
    };

    private static LoadPlan Build(Groupage groupage, PackingOptions options, string name,
        Func<Vehicle, List<PhysicalUnit>, List<StrategyOutcome>, PackResult> packer)
    {
        var sw = Stopwatch.StartNew();

        var plan = new LoadPlan { Groupage = groupage, Options = options.Clone(), Name = name };
        if (!groupage.IsComputable)
        {
            foreach (var error in groupage.BlockingErrors)
            {
                plan.Issues.Add(new Issue(IssueSeverity.Error, "VEH_INDISPENSABLE", error, groupage.SourceLine));
            }

            var all = UnitBuilder.Build(groupage, options);
            plan.Unloaded.AddRange(all.Units.Select(u => new UnloadedUnit(u, RejectReason.InvalidData, "Véhicule non défini")));
            plan.Unloaded.AddRange(all.Rejected);
            plan.Loads.Add(new VehicleLoad { Number = 1, Vehicle = groupage.Vehicle });
            plan.Loads[0].Metrics = PlanMetrics.Compute(plan, plan.Loads[0]);
            plan.Metrics = PlanMetrics.Compute(plan);
            return plan;
        }

        var built = UnitBuilder.Build(groupage, options);
        plan.Issues.AddRange(built.Issues);
        plan.Unloaded.AddRange(built.Rejected);

        // Besoin théorique en métrage linéaire par ordre (données seules, avant placement).
        foreach (var group in built.Units.Concat(built.Rejected.Select(r => r.Unit))
                     .Where(u => u.Order.IsValid)
                     .GroupBy(u => u.Order))
        {
            if (LinearMeterCalculator.ForUnits(group.ToList(), options.UsableSpace(groupage.Vehicle), options.GapBetweenUnits) is { } ml)
            {
                plan.LinearMetersByOrder[group.Key] = ml;
            }
        }

        // Camion 1, puis camions supplémentaires du même type tant qu'il reste un reliquat « de place ».
        var remaining = built.Units;
        var maxVehicles = Math.Max(1, options.MaxVehicles);
        List<UnloadedUnit> lastUnloaded = [];
        while (remaining.Count > 0 && plan.Loads.Count < maxVehicles)
        {
            var load = new VehicleLoad { Number = plan.Loads.Count + 1, Vehicle = groupage.Vehicle };
            var best = packer(groupage.Vehicle, remaining, load.StrategyLog);
            if (best.Placed.Count == 0)
            {
                lastUnloaded = best.Unloaded;
                break;
            }

            load.Strategy = best.Strategy;
            foreach (var placement in best.Placed)
            {
                placement.VehicleNumber = load.Number;
            }

            load.Placements.AddRange(best.Placed);
            plan.Loads.Add(load);
            lastUnloaded = best.Unloaded;
            remaining = best.Unloaded.Select(u => u.Unit).ToList();
        }

        if (plan.Loads.Count == 0)
        {
            plan.Loads.Add(new VehicleLoad { Number = 1, Vehicle = groupage.Vehicle });
        }

        plan.Unloaded.AddRange(lastUnloaded);
        if (lastUnloaded.Count > 0 && maxVehicles > 1 && plan.Loads.Count >= maxVehicles)
        {
            plan.Issues.Add(new Issue(IssueSeverity.Warning, "CAMIONS_MAX",
                $"Nombre maximal de camions atteint ({maxVehicles}) : {lastUnloaded.Sum(u => u.Unit.ItemCount)} article(s) restent en reliquat."));
        }

        if (plan.Loads.Count > 1)
        {
            plan.Issues.Add(new Issue(IssueSeverity.Info, "MULTI_CAMIONS",
                $"Reliquat réparti sur {plan.Loads.Count} camions {groupage.Vehicle.Id}."));
        }

        plan.Duration = sw.Elapsed;
        foreach (var load in plan.Loads)
        {
            load.Metrics = PlanMetrics.Compute(plan, load);
        }

        plan.Metrics = PlanMetrics.Compute(plan);
        return plan;
    }

    /// <summary>Remplit un camion : chaque combinaison (tri × mode) en parallèle, choix déterministe du meilleur.</summary>
    private static PackResult PackBest(Vehicle vehicle, List<PhysicalUnit> units, PackingOptions options, List<StrategyOutcome> log)
    {
        var runs = options.MultiStrategy ? AllRuns().ToArray() : [(Strategies[0], PackMode.Walls)];

        var results = new PackResult[runs.Length];
        Parallel.For(0, runs.Length, i =>
        {
            var (strategy, mode) = runs[i];
            results[i] = Pack(vehicle, strategy.Sort(units).ToList(), options, mode);
            results[i].Strategy = RunName(strategy, mode);
        });

        PackResult? best = null;
        foreach (var result in results)
        {
            log.Add(new StrategyOutcome(result.Strategy, result.Placed.Count,
                result.Placed.Sum(p => p.Unit.ItemCount), result.Placed.Sum(p => p.Unit.Volume) / 1e9, result.MaxX / 1000,
                CompleteOrders(result)));
            if (best == null || IsBetter(result, best))
            {
                best = result;
            }
        }

        return best!;
    }

    private static IOrderedEnumerable<PhysicalUnit> Base(IEnumerable<PhysicalUnit> units) =>
        // Chargé plus tôt au fond, zone de rangement, livré plus tard au fond (0 = sans arrêt, côté porte).
        // Sans étapes ni zones, identique au tri historique (arrêt décroissant).
        units.OrderBy(u => u.Departure)
            .ThenBy(u => u.GroupRank)
            .ThenByDescending(u => u.Arrival)
            .ThenBy(u => u.MustBeOnTop)
            .ThenByDescending(u => u.MustBeOnFloor);

    private static double FootprintMax(PhysicalUnit u) =>
        u.Orientations.Enumerate().Max(o =>
        {
            var (dx, dy, _) = Orientations.Apply(o, u.A, u.B, u.C);
            return dx * dy;
        });

    /// <summary>
    /// Meilleur plan : le plus d'ordres chargés en totalité, puis le plus de volume chargé, puis le métrage le plus court.
    /// (Le nombre d'articles serait biaisé par les piles de plaques.)
    /// </summary>
    private static bool IsBetter(PackResult a, PackResult b)
    {
        var completeA = CompleteOrders(a);
        var completeB = CompleteOrders(b);
        if (completeA != completeB)
        {
            return completeA > completeB;
        }

        var volA = a.Placed.Sum(p => p.Unit.Volume);
        var volB = b.Placed.Sum(p => p.Unit.Volume);
        if (Math.Abs(volA - volB) > 1)
        {
            return volA > volB;
        }

        return a.MaxX < b.MaxX - Geometry.Eps;
    }

    private static int CompleteOrders(PackResult r) =>
        r.Placed.Select(p => p.Unit.Order).Distinct().Count(o => r.Unloaded.All(u => u.Unit.Order != o));

    private sealed class PackResult
    {
        public string Strategy { get; set; } = "";
        public List<Placement> Placed { get; } = [];
        public List<UnloadedUnit> Unloaded { get; } = [];
        public double MaxX => Placed.Count == 0 ? 0 : Placed.Max(p => p.MaxX);
    }

    private static PackResult Pack(Vehicle v, List<PhysicalUnit> units, PackingOptions options, PackMode mode)
    {
        var pinned = mode == PackMode.Rows ? RowOrientations(units, options.UsableSpace(v), options.GapBetweenUnits) : null;
        var state = new PackingState(v, options, mode == PackMode.Columns, pinned);
        var result = new PackResult();
        var failed = new Dictionary<string, RejectReason>();

        foreach (var unit in units)
        {
            if (failed.TryGetValue(unit.ShapeKey, out var known))
            {
                result.Unloaded.Add(new UnloadedUnit(unit, known));
                continue;
            }

            if (state.TotalWeight + unit.Weight > v.MaxPayload + Geometry.Eps)
            {
                result.Unloaded.Add(new UnloadedUnit(unit, RejectReason.PayloadExceeded,
                    $"{state.TotalWeight + unit.Weight:0.#} kg > {v.MaxPayload:0} kg"));
                continue;
            }

            var placement = state.TryPlace(unit);
            if (placement == null)
            {
                // Motif : la place existe mais le rangement (étapes, zones) l'interdit, ou il n'y a plus de place.
                var reason = state.FitsIgnoringStowage(unit) ? RejectReason.StowageBlocked
                    : unit.CanBeElevated ? RejectReason.NoValidPosition
                    : RejectReason.FloorSaturatedNonStackable;
                failed[unit.ShapeKey] = reason;
                result.Unloaded.Add(new UnloadedUnit(unit, reason));
                continue;
            }

            placement.Sequence = result.Placed.Count + 1;
            result.Placed.Add(placement);
        }

        return result;
    }

    /// <summary>
    /// Sens de pose qui minimise le métrage par rangées de chaque ordre (ex. palettes 1995 × 1200 : deux de front
    /// dans la longueur plutôt qu'une seule en travers). Le placement unité par unité ne le voit pas seul.
    /// </summary>
    private static Dictionary<TransportOrder, int> RowOrientations(List<PhysicalUnit> units, Vehicle v, double gap)
    {
        var map = new Dictionary<TransportOrder, int>();
        foreach (var group in units.GroupBy(u => u.Order))
        {
            if (LinearMeterCalculator.ForUnits(group.ToList(), v, gap) is { } ml)
            {
                map[group.Key] = ml.BestOrientation;
            }
        }

        return map;
    }

    /// <summary>État incrémental d'un remplissage.</summary>
    /// <param name="columnFirst">false = parois (remplit la largeur au sol avant de gerber), true = colonnes (gerbe d'abord).</param>
    /// <param name="pinned">Sens de pose imposé par ordre (mode rangées), sinon toutes les orientations autorisées.</param>
    private sealed class PackingState(Vehicle vehicle, PackingOptions options, bool columnFirst,
        IReadOnlyDictionary<TransportOrder, int>? pinned = null)
    {
        // Débords : bande libre le long des parois (Y), sous le plafond (Z) et jeu entre unités voisines.
        private readonly double _gap = Math.Max(0, options.GapBetweenUnits);
        private readonly double _minY = Math.Max(0, options.SideClearance);
        private readonly double _maxY = vehicle.Width - Math.Max(0, options.SideClearance);
        private readonly double _maxZ = vehicle.Height - Math.Max(0, options.RoofClearance);
        private readonly List<Placement> _placed = [];
        private readonly List<(double X, double Y, double Z)> _points = [(0, Math.Max(0, options.SideClearance), 0)];
        private readonly HashSet<(long, long, long)> _pointKeys = [(0, (long)Math.Round(Math.Max(0, options.SideClearance) * 10), 0)];
        private readonly bool _deliveryOrder = options.RespectDeliveryOrder;

        public double TotalWeight { get; private set; }

        public Placement? TryPlace(PhysicalUnit unit) => FindBest(unit, false) is { } best ? Commit(unit, best) : null;

        /// <summary>Une position existerait-elle sans les contraintes d'accessibilité et de zones ?</summary>
        public bool FitsIgnoringStowage(PhysicalUnit unit) => FindBest(unit, true) != null;

        private Candidate? FindBest(PhysicalUnit unit, bool ignoreStowage)
        {
            var allowed = pinned != null && pinned.TryGetValue(unit.Order, out var pin) &&
                          unit.Orientations.Contains(pin) && UnitBuilder.PassesDoor(unit, pin, vehicle)
                ? [pin]
                : unit.Orientations.Enumerate();
            var orientations = allowed
                .Where(o => UnitBuilder.PassesDoor(unit, o, vehicle))
                .Select(o => (O: o, D: Orientations.Apply(o, unit.A, unit.B, unit.C)))
                .GroupBy(x => x.D)
                .Select(g => g.First())
                .ToList();

            if (orientations.Count == 0)
            {
                return null;
            }

            var minDx = orientations.Min(x => x.D.DX);
            Candidate? best = null;

            foreach (var p in _points)
            {
                if (best != null && p.X + minDx > best.Score1 + Geometry.Eps)
                {
                    break;
                }

                foreach (var (o, (dx, dy, dz)) in orientations)
                {
                    if (p.X + dx > vehicle.Length + Geometry.Eps ||
                        p.Y < _minY - Geometry.Eps ||
                        p.Y + dy > _maxY + Geometry.Eps ||
                        p.Z + dz > _maxZ + Geometry.Eps)
                    {
                        continue;
                    }

                    var candidate = new Candidate(p.X, p.Y, p.Z, dx, dy, dz, o);
                    if (best != null && !candidate.BetterThan(best, columnFirst))
                    {
                        continue;
                    }

                    if (p.Z > Geometry.Eps && !unit.CanBeElevated)
                    {
                        continue;
                    }

                    if (Collides(candidate))
                    {
                        continue;
                    }

                    if (!CheckSupport(unit, candidate))
                    {
                        continue;
                    }

                    if (!ignoreStowage && Blocks(unit, candidate))
                    {
                        continue;
                    }

                    best = candidate;
                }
            }

            return best;
        }

        /// <summary>
        /// Collision, ou contact par le dessus avec une unité déjà posée : glisser une unité sous un
        /// porte-à-faux en ferait un support a posteriori (charge et gerbabilité non contrôlées).
        /// </summary>
        private bool Collides(Candidate c)
        {
            var top = c.Z + c.DZ;
            foreach (var q in _placed)
            {
                var overlapXY = Geometry.Overlap(c.X, c.X + c.DX, q.X, q.MaxX) > Geometry.Eps &&
                                Geometry.Overlap(c.Y, c.Y + c.DY, q.Y, q.MaxY) > Geometry.Eps;
                var sameLevel = Geometry.Overlap(c.Z, top, q.Z, q.MaxZ) > Geometry.Eps;

                // Débord entre unités : au même niveau, les emprises agrandies du jeu ne doivent pas se toucher.
                if (sameLevel && _gap > 0 && Clearance.TooClose(c.X, c.Y, c.DX, c.DY, q, _gap))
                {
                    return true;
                }

                if (overlapXY && (sameLevel || Math.Abs(q.Z - top) <= Geometry.Eps))
                {
                    return true;
                }
            }

            return false;
        }

        private bool CheckSupport(PhysicalUnit unit, Candidate c)
        {
            c.Supports.Clear();
            if (c.Z <= Geometry.Eps)
            {
                c.Level = 1;
                c.LevelCap = unit.MaxLevels;
                return true;
            }

            double contact = 0;
            foreach (var q in _placed)
            {
                if (Math.Abs(q.MaxZ - c.Z) > Geometry.Eps)
                {
                    continue;
                }

                var area = Geometry.Overlap(c.X, c.X + c.DX, q.X, q.MaxX) * Geometry.Overlap(c.Y, c.Y + c.DY, q.Y, q.MaxY);
                if (area <= Geometry.Eps)
                {
                    continue;
                }

                if (StackingRules.Forbidden(unit, c.Orientation, c.X, c.Y, c.DX, c.DY, q) != null)
                {
                    return false;
                }

                c.Supports.Add(new SupportLink(q, area));
                contact += area;
            }

            if (c.Supports.Count == 0 || contact / (c.DX * c.DY) < unit.RequiredSupportRatio - 1e-9 ||
                !StackingRules.CenterOverSupports(c.X, c.Y, c.DX, c.DY, c.Supports.Select(s => s.Support)))
            {
                return false;
            }

            c.Level = 1 + c.Supports.Max(s => s.Support.Level);
            c.LevelCap = Math.Min(unit.MaxLevels, c.Supports.Min(s => s.Support.LevelCap));
            if (c.Level > c.LevelCap)
            {
                return false;
            }

            // Propagation descendante du poids, au prorata des surfaces de contact.
            var deltas = new Dictionary<Placement, double>();
            Distribute(c.Supports, unit.Weight, deltas);
            foreach (var (q, delta) in deltas)
            {
                if (q.LoadAbove + delta > q.Unit.MaxLoadOnTop + 1e-6)
                {
                    return false;
                }
            }

            c.LoadDeltas = deltas;
            return true;
        }

        private static void Distribute(List<SupportLink> supports, double weight, Dictionary<Placement, double> deltas)
        {
            var total = supports.Sum(s => s.ContactArea);
            foreach (var s in supports)
            {
                var share = weight * s.ContactArea / total;
                deltas[s.Support] = deltas.GetValueOrDefault(s.Support) + share;
                if (s.Support.Supports.Count > 0)
                {
                    Distribute(s.Support.Supports, share, deltas);
                }
            }
        }

        /// <summary>
        /// Accessibilité par la porte arrière (chargement et déchargement à chaque étape) et zones de rangement.
        /// Sans étapes ni zones, se réduit à la règle historique : aucune unité d'un arrêt ultérieur entre l'unité et la porte.
        /// </summary>
        private bool Blocks(PhysicalUnit unit, Candidate c)
        {
            var access = _deliveryOrder && options.Stowage != StowageMode.Compact &&
                         (unit.Departure > 0 || unit.Arrival > 0 || _placed.Count > 0);
            var grouped = unit.GroupRank >= 0;
            if (!access && !grouped)
            {
                return false;
            }

            var maxX = c.X + c.DX;
            foreach (var q in _placed)
            {
                var overlapYZ = Geometry.Overlap(c.Y, c.Y + c.DY, q.Y, q.MaxY) > Geometry.Eps &&
                                Geometry.Overlap(c.Z, c.Z + c.DZ, q.Z, q.MaxZ) > Geometry.Eps;
                if (access && overlapYZ)
                {
                    // q entre l'unité et la porte, ou l'unité entre q et la porte.
                    if (q.X >= maxX - Geometry.Eps && Stowage.Blocks(unit, q.Unit))
                    {
                        return true;
                    }

                    if (c.X >= q.MaxX - Geometry.Eps && Stowage.Blocks(q.Unit, unit))
                    {
                        return true;
                    }
                }

                if (grouped && Stowage.ZoneConflict(unit, c.X, maxX, q))
                {
                    return true;
                }
            }

            foreach (var s in c.Supports)
            {
                // Posée sur une unité : elle la bloque si celle-ci doit sortir avant elle ; pas de gerbage entre zones.
                if (access && Stowage.Blocks(s.Support.Unit, unit))
                {
                    return true;
                }

                if (grouped && !string.Equals(s.Support.Unit.GroupKey, unit.GroupKey, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private Placement Commit(PhysicalUnit unit, Candidate c)
        {
            var placement = new Placement
            {
                Unit = unit,
                X = c.X,
                Y = c.Y,
                Z = c.Z,
                DX = c.DX,
                DY = c.DY,
                DZ = c.DZ,
                Orientation = c.Orientation,
                Level = c.Level,
                LevelCap = c.LevelCap
            };
            placement.Supports.AddRange(c.Supports);
            if (c.LoadDeltas != null)
            {
                foreach (var (q, delta) in c.LoadDeltas)
                {
                    q.LoadAbove += delta;
                }
            }

            _placed.Add(placement);
            TotalWeight += unit.Weight;

            // Points suivants décalés du débord entre unités (pas en hauteur : le gerbage reste en contact).
            var mx = placement.MaxX + _gap;
            var my = placement.MaxY + _gap;
            var mz = placement.MaxZ;
            AddPoint(mx, c.Y, c.Z);
            AddPoint(c.X, my, c.Z);
            AddPoint(c.X, c.Y, mz);
            AddPoint(mx, ProjectY(mx, c.Y, c.Z), c.Z);
            AddPoint(mx, c.Y, ProjectZ(mx, c.Y, c.Z));
            AddPoint(ProjectX(c.X, my, c.Z), my, c.Z);
            AddPoint(c.X, my, ProjectZ(c.X, my, c.Z));
            AddPoint(ProjectX(c.X, c.Y, mz), c.Y, mz);
            AddPoint(c.X, ProjectY(c.X, c.Y, mz), mz);

            _points.RemoveAll(p => IsInside(placement, p));
            return placement;
        }

        private static bool IsInside(Placement q, (double X, double Y, double Z) p) =>
            p.X >= q.X - Geometry.Eps && p.X < q.MaxX - Geometry.Eps &&
            p.Y >= q.Y - Geometry.Eps && p.Y < q.MaxY - Geometry.Eps &&
            p.Z >= q.Z - Geometry.Eps && p.Z < q.MaxZ - Geometry.Eps;

        private void AddPoint(double x, double y, double z)
        {
            if (x >= vehicle.Length - Geometry.Eps || y >= _maxY - Geometry.Eps || z >= _maxZ - Geometry.Eps)
            {
                return;
            }

            var key = ((long)Math.Round(x * 10), (long)Math.Round(y * 10), (long)Math.Round(z * 10));
            if (!_pointKeys.Add(key) || _placed.Any(q => IsInside(q, (x, y, z))))
            {
                return;
            }

            var point = (x, y, z);
            var index = _points.BinarySearch(point, PointComparer.Instance);
            _points.Insert(index < 0 ? ~index : index, point);
        }

        private double ProjectZ(double x, double y, double z)
        {
            double best = 0;
            foreach (var q in _placed)
            {
                if (q.MaxZ <= z + Geometry.Eps && q.MaxZ > best &&
                    x >= q.X - Geometry.Eps && x < q.MaxX - Geometry.Eps &&
                    y >= q.Y - Geometry.Eps && y < q.MaxY - Geometry.Eps)
                {
                    best = q.MaxZ;
                }
            }

            return best;
        }

        private double ProjectY(double x, double y, double z)
        {
            var best = _minY;
            foreach (var q in _placed)
            {
                var edge = q.MaxY + _gap;
                if (edge <= y + Geometry.Eps && edge > best &&
                    x >= q.X - Geometry.Eps && x < q.MaxX - Geometry.Eps &&
                    z >= q.Z - Geometry.Eps && z < q.MaxZ - Geometry.Eps)
                {
                    best = edge;
                }
            }

            return best;
        }

        private double ProjectX(double x, double y, double z)
        {
            double best = 0;
            foreach (var q in _placed)
            {
                var edge = q.MaxX + _gap;
                if (edge <= x + Geometry.Eps && edge > best &&
                    y >= q.Y - Geometry.Eps && y < q.MaxY - Geometry.Eps &&
                    z >= q.Z - Geometry.Eps && z < q.MaxZ - Geometry.Eps)
                {
                    best = edge;
                }
            }

            return best;
        }
    }

    private sealed class Candidate(double x, double y, double z, double dx, double dy, double dz, int orientation)
    {
        public double X { get; } = x;
        public double Y { get; } = y;
        public double Z { get; } = z;
        public double DX { get; } = dx;
        public double DY { get; } = dy;
        public double DZ { get; } = dz;
        public int Orientation { get; } = orientation;
        public int Level { get; set; } = 1;
        public int LevelCap { get; set; }
        public List<SupportLink> Supports { get; } = [];
        public Dictionary<Placement, double>? LoadDeltas { get; set; }

        /// <summary>Critère principal : remplir l'avant du véhicule (paroi par paroi).</summary>
        public double Score1 => X + DX;

        public bool BetterThan(Candidate other, bool columnFirst)
        {
            if (Math.Abs(Score1 - other.Score1) > Geometry.Eps)
            {
                return Score1 < other.Score1;
            }

            if (columnFirst && Math.Abs(Y - other.Y) > Geometry.Eps)
            {
                return Y < other.Y;
            }

            if (Math.Abs(Z - other.Z) > Geometry.Eps)
            {
                return Z < other.Z;
            }

            if (Math.Abs(Y - other.Y) > Geometry.Eps)
            {
                return Y < other.Y;
            }

            return DZ < other.DZ - Geometry.Eps;
        }
    }

    private sealed class PointComparer : IComparer<(double X, double Y, double Z)>
    {
        public static readonly PointComparer Instance = new();

        public int Compare((double X, double Y, double Z) a, (double X, double Y, double Z) b)
        {
            var c = a.X.CompareTo(b.X);
            if (c != 0)
            {
                return c;
            }

            c = a.Z.CompareTo(b.Z);
            return c != 0 ? c : a.Y.CompareTo(b.Y);
        }
    }
}
