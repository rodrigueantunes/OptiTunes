using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using OptiTunes.Core.Models;

namespace OptiTunes.App.Services;

public sealed class SceneResult
{
    public Model3DGroup Root { get; } = new();
    public Dictionary<GeometryModel3D, Placement> ByModel { get; } = [];
    public Dictionary<string, GeometryModel3D> ByUnit { get; } = [];
}

/// <summary>
/// Construit la scène 3D d'un camion (mètres, Z vers le haut) : plancher, arêtes de la caisse, cabine à l'avant,
/// porte arrière en orange, une géométrie par unité (pavé ou cylindre). Les matériaux sont posés par l'appelant.
/// </summary>
public static class Scene3DBuilder
{
    private const double Scale = 0.001;
    private const double Gap = 0.006;

    private static readonly Dictionary<Color, Material> Cache = [];

    public static readonly Material Selected = Freeze(new MaterialGroup
    {
        Children =
        {
            new DiffuseMaterial(new SolidColorBrush(Color.FromRgb(0xFF, 0xD5, 0x00))),
            new EmissiveMaterial(new SolidColorBrush(Color.FromRgb(0x90, 0x60, 0x00)))
        }
    });

    public static readonly Material Hover = Freeze(new MaterialGroup
    {
        Children =
        {
            new DiffuseMaterial(new SolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0xFF))),
            new EmissiveMaterial(new SolidColorBrush(Color.FromRgb(0x30, 0x60, 0x90)))
        }
    });

    public static Material Solid(Color color)
    {
        if (!Cache.TryGetValue(color, out var material))
        {
            material = Freeze(new MaterialGroup
            {
                Children =
                {
                    new DiffuseMaterial(new SolidColorBrush(color)),
                    new SpecularMaterial(new SolidColorBrush(Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF)), 30)
                }
            });
            Cache[color] = material;
        }

        return material;
    }

    public static SceneResult Build(VehicleLoad load, int visibleCount, PackingOptions? options = null)
    {
        var scene = new SceneResult();
        var v = load.Vehicle;
        var l = v.Length * Scale;
        var w = v.Width * Scale;
        var h = v.Height * Scale;

        // Plancher
        scene.Root.Children.Add(Model(Box(0, 0, -0.03, l, w, 0), Solid(Color.FromRgb(0x95, 0xA5, 0xA6))));

        // Cabine (avant, X < 0)
        scene.Root.Children.Add(Model(Box(-1.9, 0.05, -0.03, -0.15, w - 0.05, Math.Min(h, 2.6)),
            Solid(Color.FromRgb(0x34, 0x49, 0x5E))));

        // Arêtes de la caisse et cadre de porte
        var edges = new MeshGeometry3D();
        const double t = 0.015;
        AddBar(edges, 0, 0, h, l, 0, h, t);
        AddBar(edges, 0, w, h, l, w, h, t);
        AddBar(edges, 0, 0, 0, 0, 0, h, t);
        AddBar(edges, 0, w, 0, 0, w, h, t);
        AddBar(edges, 0, 0, h, 0, w, h, t);
        AddBar(edges, 0, 0, 0, l, 0, 0, t);
        AddBar(edges, 0, w, 0, l, w, 0, t);
        scene.Root.Children.Add(Model(edges, Solid(Color.FromRgb(0x5D, 0x6D, 0x7E))));

        var door = new MeshGeometry3D();
        var dw = (v.DoorWidth ?? v.Width) * Scale;
        var dh = (v.DoorHeight ?? v.Height) * Scale;
        var dy = (w - dw) / 2;
        AddBar(door, l, dy, 0, l, dy, dh, t * 2);
        AddBar(door, l, dy + dw, 0, l, dy + dw, dh, t * 2);
        AddBar(door, l, dy, dh, l, dy + dw, dh, t * 2);
        scene.Root.Children.Add(Model(door, Solid(Color.FromRgb(0xF3, 0x9C, 0x12))));

        // Débords : lignes au sol le long des parois et cadre sous le plafond.
        if (options is { HasClearances: true })
        {
            var limits = new MeshGeometry3D();
            var s = options.SideClearance * Scale;
            var top = h - options.RoofClearance * Scale;
            const double thin = 0.006;
            if (s > 0)
            {
                AddBar(limits, 0, s, 0.002, l, s, 0.002, thin);
                AddBar(limits, 0, w - s, 0.002, l, w - s, 0.002, thin);
            }

            if (options.RoofClearance > 0)
            {
                AddBar(limits, 0, s, top, l, s, top, thin);
                AddBar(limits, 0, w - s, top, l, w - s, top, thin);
                AddBar(limits, 0, s, top, 0, w - s, top, thin);
                AddBar(limits, l, s, top, l, w - s, top, thin);
            }

            scene.Root.Children.Add(Model(limits, Solid(Color.FromRgb(0xE6, 0x7E, 0x22))));
        }

        foreach (var p in load.Placements.Where(p => p.Sequence <= visibleCount))
        {
            var mesh = p.Unit.Shape switch
            {
                UnitShape.Cylinder => Cylinder(p),
                UnitShape.Staggered => StaggeredBed(p),
                _ => Box(p.X * Scale + Gap, p.Y * Scale + Gap, p.Z * Scale + Gap,
                    p.MaxX * Scale - Gap, p.MaxY * Scale - Gap, p.MaxZ * Scale - Gap)
            };
            var model = Model(mesh, Solid(Colors.SteelBlue));
            scene.Root.Children.Add(model);
            scene.ByModel[model] = p;
            scene.ByUnit[p.Unit.Id] = model;
        }

        return scene;
    }

    private static GeometryModel3D Model(MeshGeometry3D mesh, Material material)
    {
        mesh.Freeze();
        return new GeometryModel3D(mesh, material) { BackMaterial = material };
    }

    private static T Freeze<T>(T freezable) where T : Freezable
    {
        freezable.Freeze();
        return freezable;
    }

    private static MeshGeometry3D Box(double x0, double y0, double z0, double x1, double y1, double z1)
    {
        var mesh = new MeshGeometry3D();
        AddBox(mesh, x0, y0, z0, x1, y1, z1);
        return mesh;
    }

    /// <summary>Pavé à faces plates (4 sommets par face pour un ombrage net).</summary>
    private static void AddBox(MeshGeometry3D mesh, double x0, double y0, double z0, double x1, double y1, double z1)
    {
        AddQuad(mesh, new(x0, y0, z0), new(x0, y1, z0), new(x1, y1, z0), new(x1, y0, z0));
        AddQuad(mesh, new(x0, y0, z1), new(x1, y0, z1), new(x1, y1, z1), new(x0, y1, z1));
        AddQuad(mesh, new(x0, y0, z0), new(x1, y0, z0), new(x1, y0, z1), new(x0, y0, z1));
        AddQuad(mesh, new(x1, y1, z0), new(x0, y1, z0), new(x0, y1, z1), new(x1, y1, z1));
        AddQuad(mesh, new(x0, y1, z0), new(x0, y0, z0), new(x0, y0, z1), new(x0, y1, z1));
        AddQuad(mesh, new(x1, y0, z0), new(x1, y1, z0), new(x1, y1, z1), new(x1, y0, z1));
    }

    private static void AddQuad(MeshGeometry3D mesh, Point3D a, Point3D b, Point3D c, Point3D d)
    {
        var i = mesh.Positions.Count;
        var n = Vector3D.CrossProduct(b - a, d - a);
        n.Normalize();
        foreach (var p in new[] { a, b, c, d })
        {
            mesh.Positions.Add(p);
            mesh.Normals.Add(n);
        }

        mesh.TriangleIndices.Add(i);
        mesh.TriangleIndices.Add(i + 1);
        mesh.TriangleIndices.Add(i + 2);
        mesh.TriangleIndices.Add(i);
        mesh.TriangleIndices.Add(i + 2);
        mesh.TriangleIndices.Add(i + 3);
    }

    private static void AddBar(MeshGeometry3D mesh, double x0, double y0, double z0, double x1, double y1, double z1, double t) =>
        AddBox(mesh, Math.Min(x0, x1) - t, Math.Min(y0, y1) - t, Math.Min(z0, z1) - t,
            Math.Max(x0, x1) + t, Math.Max(y0, y1) + t, Math.Max(z0, z1) + t);

    /// <summary>Cylindre inscrit dans l'enveloppe, selon l'axe orienté du tube ou de la bobine.</summary>
    private static MeshGeometry3D Cylinder(Placement p)
    {
        var axis = Math.Max(0, p.CylinderAxis);
        double[] min = [p.X * Scale, p.Y * Scale, p.Z * Scale];
        double[] size = [p.DX * Scale, p.DY * Scale, p.DZ * Scale];
        var u = (axis + 1) % 3;
        var v = (axis + 2) % 3;
        var mesh = new MeshGeometry3D();
        AddCylinder(mesh, axis, min[axis] + Gap, min[axis] + size[axis] - Gap,
            min[u] + size[u] / 2, min[v] + size[v] / 2, Math.Min(size[u], size[v]) / 2 - Gap);
        return mesh;
    }

    /// <summary>Lit de tubes en quinconce : un cylindre par tube, dans une seule géométrie (sélection du lit entier).</summary>
    private static MeshGeometry3D StaggeredBed(Placement p)
    {
        var axis = Math.Max(0, p.CylinderAxis);
        var u = (axis + 1) % 3;
        var v = (axis + 2) % 3;
        double[] min = [p.X * Scale, p.Y * Scale, p.Z * Scale];
        double[] size = [p.DX * Scale, p.DY * Scale, p.DZ * Scale];
        var radius = p.Unit.TubeDiameter * Scale / 2 - Gap / 2;
        var mesh = new MeshGeometry3D();
        foreach (var (x, y, z) in p.TubeCentersWorld())
        {
            double[] c = [x * Scale, y * Scale, z * Scale];
            AddCylinder(mesh, axis, min[axis] + Gap, min[axis] + size[axis] - Gap, c[u], c[v], radius, 16);
        }

        return mesh;
    }

    private static void AddCylinder(MeshGeometry3D mesh, int axis, double a0, double a1, double cu, double cv, double radius, int segments = 24)
    {
        var u = (axis + 1) % 3;
        var v = (axis + 2) % 3;

        Point3D P(double a, double uu, double vv)
        {
            var c = new double[3];
            c[axis] = a;
            c[u] = uu;
            c[v] = vv;
            return new Point3D(c[0], c[1], c[2]);
        }

        Vector3D N(double nu, double nv)
        {
            var c = new double[3];
            c[u] = nu;
            c[v] = nv;
            return new Vector3D(c[0], c[1], c[2]);
        }

        var axisDir = new double[3];
        axisDir[axis] = 1;
        var axisVec = new Vector3D(axisDir[0], axisDir[1], axisDir[2]);

        for (var i = 0; i < segments; i++)
        {
            var t0 = 2 * Math.PI * i / segments;
            var t1 = 2 * Math.PI * (i + 1) / segments;
            var (c0, s0, c1, s1) = (Math.Cos(t0), Math.Sin(t0), Math.Cos(t1), Math.Sin(t1));

            var k = mesh.Positions.Count;
            mesh.Positions.Add(P(a0, cu + radius * c0, cv + radius * s0));
            mesh.Positions.Add(P(a0, cu + radius * c1, cv + radius * s1));
            mesh.Positions.Add(P(a1, cu + radius * c1, cv + radius * s1));
            mesh.Positions.Add(P(a1, cu + radius * c0, cv + radius * s0));
            mesh.Normals.Add(N(c0, s0));
            mesh.Normals.Add(N(c1, s1));
            mesh.Normals.Add(N(c1, s1));
            mesh.Normals.Add(N(c0, s0));
            foreach (var idx in new[] { 0, 1, 2, 0, 2, 3 })
            {
                mesh.TriangleIndices.Add(k + idx);
            }

            foreach (var (a, normal) in new[] { (a0, -axisVec), (a1, axisVec) })
            {
                var j = mesh.Positions.Count;
                mesh.Positions.Add(P(a, cu, cv));
                mesh.Positions.Add(P(a, cu + radius * c0, cv + radius * s0));
                mesh.Positions.Add(P(a, cu + radius * c1, cv + radius * s1));
                for (var n = 0; n < 3; n++)
                {
                    mesh.Normals.Add(normal);
                }

                mesh.TriangleIndices.Add(j);
                mesh.TriangleIndices.Add(j + 1);
                mesh.TriangleIndices.Add(j + 2);
            }
        }
    }
}
