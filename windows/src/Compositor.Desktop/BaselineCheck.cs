using Avalonia.Headless;
using Avalonia.Media.Imaging;
using System.Buffers.Binary;
using Compositor.Core.IO;
using Compositor.Core.Model;
using Compositor.Core.Rendering;

namespace Compositor.Desktop;

internal static partial class Program
{
    /// <summary>A synthetic project kept beside the picture, so the checked document can also be opened.</summary>
    private static int Baseline(string output)
    {
        BuildHeadless().SetupWithoutStarting();
        var project = Path.ChangeExtension(Path.GetFullPath(output), ".comp");
        if (Directory.Exists(project) || File.Exists(project))
        {
            Console.Error.WriteLine("The baseline project already exists; choose a new output path.");
            return 1;
        }
        using (var document = Demo()) ProjectStore.Save(ProjectSnapshot.FromDocument(document), project);
        var window = new MainWindow();
        window.Show();
        Console.WriteLine(window.BaselineSelfCheck(project));
        using var frame = window.CaptureRenderedFrame()
            ?? throw new InvalidOperationException("The baseline window did not draw a frame.");
        frame.Save(output, new PngBitmapEncoderOptions());
        Console.WriteLine($"wrote {output}; synthetic project {project}");
        window.Close();
        return 0;
    }
}

public sealed partial class MainWindow
{
    /// <summary>
    /// The File and Layer menu paths, with exact state and pixel readback. A dirty external change is kept
    /// separate: opening it creates a second tab and leaves the human draft and its undo history in the first.
    /// </summary>
    internal string BaselineSelfCheck(string project)
    {
        var report = new List<string>();
        void Require(bool condition, string failure)
        {
            if (!condition) throw new InvalidOperationException(failure);
        }
        byte[] Rendered(CanvasDocument document)
        {
            using var image = DocumentRenderer.Render(document);
            var pixels = new byte[8 + image.Width * image.Height * 4];
            BinaryPrimitives.WriteInt32LittleEndian(pixels.AsSpan(0, 4), image.Width);
            BinaryPrimitives.WriteInt32LittleEndian(pixels.AsSpan(4, 4), image.Height);
            var at = 8;
            for (var y = 0; y < image.Height; y++)
            {
                for (var x = 0; x < image.Width; x++)
                {
                    var pixel = image.GetPixel(x, y);
                    pixels[at++] = pixel.Red;
                    pixels[at++] = pixel.Green;
                    pixels[at++] = pixel.Blue;
                    pixels[at++] = pixel.Alpha;
                }
            }
            return pixels;
        }
        void WriteExternal(string name)
        {
            using var external = ProjectStore.Load(project).ToDocument();
            external.Layers.First(layer => layer.Mask is not null).Name = name;
            ProjectStore.Save(ProjectSnapshot.FromDocument(external), project);
        }

        Open(project);
        var document = _document ?? throw new InvalidOperationException("The baseline project did not open.");
        Require(_rows.Count == document.Layers.Count && document.Layers.Count == 3, "The layers panel lost a layer.");
        Require(!_history.IsModified && !_history.CanUndo, "An opened project did not start clean.");
        var masked = document.Layers.First(layer => layer.Mask is not null);
        var id = masked.ID;
        Reselect(id);
        _canvas.Fit();
        var original = Rendered(document);
        report.Add($"PASS open: {document.Width}x{document.Height}, three layers including a group and a grayscale mask");

        ToggleMask();
        var unmasked = Rendered(document);
        Require(!unmasked.AsSpan().SequenceEqual(original) && document.Layers.First(layer => layer.ID == id).Mask?.IsEnabled == false,
            "Disabling the mask did not change the rendered pixels.");
        Require(_history.UndoCount == 1 && _history.IsModified, "The mask edit did not create one undo step.");
        Undo();
        Require(Rendered(document).AsSpan().SequenceEqual(original) && !_history.IsModified, "Undo did not restore the saved masked pixels.");
        Redo();
        Require(Rendered(document).AsSpan().SequenceEqual(unmasked) && _history.IsModified, "Redo did not restore the unmasked pixels.");
        report.Add("PASS layer mask: rendered pixels change, Undo restores them, Redo reapplies them");

        Save();
        Require(!_history.IsModified, "Saving left the document modified.");
        using (var readback = ProjectStore.Load(project).ToDocument())
        {
            Require(Rendered(readback).AsSpan().SequenceEqual(unmasked), "The saved project rendered differently after readback.");
            Require(readback.Layers.First(layer => layer.ID == id).Mask?.IsEnabled == false,
                "The saved mask state was lost.");
        }
        report.Add("PASS save/open: mask state and exact rendered pixels survive disk readback");

        // A clean file reload is allowed, and clears even saved undo history while keeping the view.
        var viewport = _canvas.Viewport;
        WriteExternal("External clean version");
        ProjectWritten(null, EventArgs.Empty);
        Require(!ReferenceEquals(document, _document), "A clean external change did not reload.");
        Require(_document!.Layers.First(layer => layer.ID == id).Name == "External clean version",
            "The external layer metadata did not arrive.");
        Require(!_history.CanUndo && !_history.CanRedo && !_history.IsModified, "Reload did not clear undo and redo.");
        Require(_canvas.Viewport == viewport, "Reload changed the viewport.");
        Require(Selected == id, "Reload changed the selected layer.");
        report.Add("PASS clean reload: external metadata arrives, viewport/selection survive, undo/redo clear");

        // Someone writing the package cannot throw away the person's in-memory draft.
        document = _document!;
        Edit("Human draft", () =>
        {
            document.Layers.First(layer => layer.ID == id).Name = "Human unsaved draft";
            return true;
        });
        var draft = _open;
        WriteExternal("External candidate version");
        ProjectWritten(null, EventArgs.Empty);
        Require(ReferenceEquals(document, _document) && _history.IsModified && _history.CanUndo,
            "A dirty external change replaced the human draft or its history.");
        Require(document.Layers.First(layer => layer.ID == id).Name == "Human unsaved draft",
            "The human layer metadata was overwritten.");
        using (var readback = ProjectStore.Load(project).ToDocument())
        {
            Require(readback.Layers.First(layer => layer.ID == id).Name == "External candidate version",
                "The external version was overwritten by the reload check.");
        }
        report.Add("PASS dirty reload: human draft and undo remain in memory; external candidate remains on disk");

        Open(project);
        Require(_tabs.Count == 2 && !ReferenceEquals(draft, _open), "Reopening did not create a separate comparison tab.");
        Require(draft.History.IsModified && draft.Document!.Layers.First(layer => layer.ID == id).Name == "Human unsaved draft",
            "Opening the candidate discarded the draft tab.");
        Require(_document!.Layers.First(layer => layer.ID == id).Name == "External candidate version" && !_history.IsModified,
            "The comparison tab did not open the external version cleanly.");
        report.Add("PASS reopen: candidate opens in a second tab; the first still contains the unsaved human version");
        report.Add("LIMIT: a later Save can overwrite the external version; revision/conflict adoption needs an adapter");

        // Only synthetic self-check data is discarded when this temporary window is closed.
        draft.History.MarkSaved();
        return string.Join(Environment.NewLine, report);
    }
}
