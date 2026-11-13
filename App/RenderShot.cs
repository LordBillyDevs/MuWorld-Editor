using System.Drawing.Imaging;
using System.Numerics;
using System.Runtime.InteropServices;
using MuWorldEditor.Formats;
using MuWorldEditor.Render;

namespace MuWorldEditor.App;

/// <summary>
/// Dibuja un mapa y guarda la imagen en un archivo. Sirve para comprobar el
/// render sin tener que abrir la ventana a mano.
/// </summary>
public static class RenderShot
{
    public static unsafe void Run(string dataPath, int world, string outFile, bool attributes, bool grid, float dist = 3500f)
    {
        var form = new Form { Width = 1280, Height = 800, StartPosition = FormStartPosition.CenterScreen, Text = "render" };
        var view = new MapView { Dock = DockStyle.Fill };
        form.Controls.Add(view);
        form.Show();
        Application.DoEvents();

        view.LoadWorld(dataPath, world);
        view.State.ShowAttributes = attributes;
        view.State.ShowGrid = grid;
        view.Camera.Distance = dist;
        view.Camera.Pitch = 0.7f;

        // unos cuantos frames para que terminen de subirse texturas y modelos
        for (int i = 0; i < 6; i++)
        {
            view.Refresh();
            Application.DoEvents();
        }

        view.MakeCurrent();
        view.Invalidate();
        view.Refresh();

        int w = view.Width, h = view.Height;
        var pixels = new byte[w * h * 4];
        fixed (byte* p = pixels)
            Gl.ReadPixels(0, 0, w, h, Gl.RGBA, Gl.UNSIGNED_BYTE, p);

        using var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        var data = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        var row = new byte[w * 4];
        for (int y = 0; y < h; y++)
        {
            int src = (h - 1 - y) * w * 4;      // OpenGL entrega la imagen al reves
            for (int x = 0; x < w; x++)
            {
                row[x * 4 + 0] = pixels[src + x * 4 + 2];
                row[x * 4 + 1] = pixels[src + x * 4 + 1];
                row[x * 4 + 2] = pixels[src + x * 4 + 0];
                row[x * 4 + 3] = 255;
            }
            Marshal.Copy(row, 0, data.Scan0 + y * data.Stride, w * 4);
        }
        bmp.UnlockBits(data);
        bmp.Save(outFile, ImageFormat.Png);

        var wd = view.Ref.World;
        File.WriteAllText(Path.ChangeExtension(outFile, ".txt"),
            $"World{world}\r\n" +
            $"objetos: {wd.Objects.Count}\r\n" +
            $"objetos visibles: {view.VisibleObjects}\r\n" +
            $"tiles con textura: {view.Tiles?.Available.Count()}\r\n" +
            $"tipos sin modelo: {view.Catalog?.Entries.Values.Count(e => !e.Exists)}\r\n" +
            $"gl: {view.GlInfo}\r\n" +
            $"ms por cuadro: {view.LastFrameMs}\r\n" +
            $"gizmo visible: {view.Gizmo.Visible}  origen: {view.Gizmo.Origin}  largo: {view.Gizmo.Length}\r\n" +
            $"modo: {view.State.Mode}  seleccionados: {view.State.Selection.Count}\r\n");

        form.Close();
    }
}
