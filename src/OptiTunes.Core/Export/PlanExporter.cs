using System.Globalization;
using System.Text;
using OptiTunes.Core.Engine;
using OptiTunes.Core.Models;

namespace OptiTunes.Core.Export;

/// <summary>Export du plan de chargement en fichier à plat (séparateur « ; », mm / kg).</summary>
public static class PlanExporter
{
    public static string ToCsv(LoadPlan plan)
    {
        var c = CultureInfo.GetCultureInfo("fr-FR");
        var m = plan.Metrics;
        var sb = new StringBuilder();
        sb.AppendLine("#PLAN;ID_GROUPAGE;VEHICULE;RANGEMENT;SOLUTION;NB_CAMIONS;POIDS_KG;TAUX_POIDS;VOLUME_M3;TAUX_VOLUME;SOL_M2;TAUX_SOL;ML_BESOIN_RANGEES;ML_BESOIN_EQUIVALENT;ML_EQUIVALENT;ML_REEL;UNITES;ARTICLES_CHARGES;ARTICLES_TOTAL;LIMITANT;DEBORD_UNITES_MM;DEBORD_PAROIS_MM;DEBORD_PLAFOND_MM");
        sb.AppendLine(string.Join(";", "PLAN", plan.Groupage.Id, plan.Vehicle.Id, plan.Options.Stowage.Label(), plan.Name, plan.Loads.Count,
            F(m.LoadedWeightKg), F(m.WeightRate), F(m.LoadedVolumeM3, "0.000"), F(m.VolumeRate), F(m.FloorUsedM2), F(m.FloorRate),
            F(m.LinearMetersRequiredRows), F(m.LinearMetersRequiredEquivalent), F(m.LinearMetersEquivalent), F(m.LinearMetersReal), m.UnitsPlaced, m.ItemsPlaced, m.ItemsTotal, m.LimitingFactor,
            F(plan.Options.GapBetweenUnits, "0"), F(plan.Options.SideClearance, "0"), F(plan.Options.RoofClearance, "0")));

        sb.AppendLine("#CAMION;NUMERO;STRATEGIE;POIDS_KG;TAUX_POIDS;VOLUME_M3;TAUX_VOLUME;ML_EQUIVALENT;ML_REEL;UNITES;ARTICLES;LIMITANT");
        foreach (var l in plan.Loads)
        {
            var lm = l.Metrics;
            sb.AppendLine(string.Join(";", "CAMION", l.Number, l.Strategy, F(lm.LoadedWeightKg), F(lm.WeightRate), F(lm.LoadedVolumeM3, "0.000"),
                F(lm.VolumeRate), F(lm.LinearMetersEquivalent), F(lm.LinearMetersReal), lm.UnitsPlaced, lm.ItemsPlaced, lm.LimitingFactor));
        }

        sb.AppendLine("#ORDRE;ID_ORDRE;TYPE_ORDRE;NUM_CDE;LIGNE_CDE;NUM_CADENCE;ARTICLE;QUANTITE;POIDS_TOTAL_KG;PILES;NIVEAUX;PILES_PAR_RANGEE;RANGEES;ML_BESOIN_RANGEES;ML_BESOIN_EQUIVALENT;ML_PLAN;CAMIONS;CHARGE;RELIQUAT;MAGASIN;DEPART;ARRIVEE");
        foreach (var o in plan.Groupage.Orders)
        {
            var ml = plan.LinearMetersByOrder.GetValueOrDefault(o);
            sb.AppendLine(string.Join(";", "ORDRE", o.Id, o.Kind.Label(), o.OrderNumber, o.OrderLine, o.CadenceNumber, o.Article,
                o.Quantity, F(Math.Max(0, o.UnitWeight) * o.Quantity), ml?.Stacks, ml?.Levels, ml?.PerRow, ml?.Rows,
                ml == null ? "" : F(ml.MlRows), ml == null ? "" : F(ml.MlEquivalent), F(plan.PlanLinearMeters(o)),
                string.Join(",", plan.VehiclesOf(o)), plan.PlacedItems(o), plan.RemainingItems(o), o.Warehouse, o.DepartureStep, o.ArrivalStep));
        }

        sb.AppendLine("#UNITE;CAMION;SEQUENCE;ID_UNITE;ID_ORDRE;TYPE_ORDRE;NUM_CDE;LIGNE_CDE;NUM_CADENCE;ARTICLE;TYPE_PHYSIQUE;NB_ARTICLES;X;Y;Z;DX;DY;DZ;NIVEAU;POIDS_KG;CHARGE_DESSUS_KG;CHARGE_MAX_KG;SUPPORTS;ARRET;MAGASIN;DEPART;ARRIVEE");
        foreach (var p in plan.Placements.OrderBy(p => p.VehicleNumber).ThenBy(p => p.Sequence))
        {
            var o = p.Unit.Order;
            sb.AppendLine(string.Join(";", "UNITE", p.VehicleNumber, p.Sequence, p.Unit.Id, o.Id, o.Kind.Label(), o.OrderNumber, o.OrderLine, o.CadenceNumber,
                o.Article, o.Type.Label(), p.Unit.ItemCount, F(p.X, "0"), F(p.Y, "0"), F(p.Z, "0"), F(p.DX, "0"), F(p.DY, "0"), F(p.DZ, "0"),
                p.Level, F(p.Unit.Weight), F(p.LoadAbove), F(p.Unit.MaxLoadOnTop),
                string.Join(",", p.Supports.Select(s => s.Support.Unit.Id)), o.Stop, o.Warehouse, o.DepartureStep, o.ArrivalStep));
        }

        sb.AppendLine("#RELIQUAT;ID_UNITE;ID_ORDRE;ARTICLE;NB_ARTICLES;MOTIF;DETAIL");
        foreach (var u in plan.Unloaded)
        {
            sb.AppendLine(string.Join(";", "RELIQUAT", u.Unit.Id, u.Unit.Order.Id, u.Unit.Order.Article, u.Unit.ItemCount,
                u.Reason.Label(), u.Detail));
        }

        return sb.ToString();

        string F(double value, string format = "0.##") => value.ToString(format, c);
    }
}
