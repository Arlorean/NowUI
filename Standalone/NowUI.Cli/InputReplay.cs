using System.Text.Json;
using System.Text.Json.Serialization;
using NowUI.Desktop;
using OpenTK.Windowing.GraphicsLibraryFramework;

namespace NowUI.Cli;

/// <summary>Replays frame-quantized client-pixel events through the same input adapter as the live window.</summary>
internal sealed class InputReplay
{
    internal sealed record Event(double Time, string Type, float X = 0, float Y = 0, string? Text = null,
        int Button = 0, float DeltaX = 0, float DeltaY = 0, string? Key = null);
    private readonly List<(int Frame, int Order, Event Value)> events = new();
    private int next;

    internal InputReplay(IEnumerable<Event> source, int fps, double endTime)
    {
        int order = 0;
        foreach (var value in source)
        {
            if (!double.IsFinite(value.Time) || value.Time < 0 || value.Time > endTime)
                throw new ArgumentException($"Replay event time must be between 0 and {endTime}: {value.Time}.");
            if (!float.IsFinite(value.X) || !float.IsFinite(value.Y) || !float.IsFinite(value.DeltaX) || !float.IsFinite(value.DeltaY))
                throw new ArgumentException("Replay coordinates and scroll deltas must be finite.");
            if ((uint)value.Button > 4) throw new ArgumentException("Replay button must be 0 (left), 1 (right), 2 (middle), 3 (back), or 4 (forward).");
            if (value.Type is not ("click" or "move" or "down" or "up" or "type" or "scroll" or "keyDown" or "keyUp"))
                throw new ArgumentException($"Unknown replay event type '{value.Type}'.");
            if (value.Type is "keyDown" or "keyUp" && (!Enum.TryParse<Keys>(value.Key, true, out var key) || !Enum.IsDefined(key)))
                throw new ArgumentException($"Unknown replay key '{value.Key}'. Use a named key, for example Enter or Tab.");
            if (value.Type == "type" && value.Text == null) throw new ArgumentException("A type event requires text.");
            int frame = (int)Math.Ceiling(value.Time * fps - 1e-8);
            if (value.Type == "click")
            {
                if (frame + 1 > (int)Math.Ceiling(endTime * fps - 1e-8))
                    throw new ArgumentException("A replay click needs a later frame for its release; increase --time or --duration.");
                events.Add((frame, order++, value with { Type = "down" }));
                events.Add((frame + 1, order++, value with { Type = "up" }));
            }
            else events.Add((frame, order++, value));
        }
        events.Sort((a, b) => a.Frame != b.Frame ? a.Frame.CompareTo(b.Frame) : a.Order.CompareTo(b.Order));
    }

    internal static InputReplay Load(string? path, int fps, double endTime)
    {
        if (path == null) return new InputReplay([], fps, endTime);
        if (new FileInfo(path).Length > 4 * 1024 * 1024) throw new ArgumentException("Replay input exceeds 4 MiB.");
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };
        var values = JsonSerializer.Deserialize<Event[]>(File.ReadAllText(path), options)
            ?? throw new ArgumentException("Replay input must contain a JSON array of events.");
        return new InputReplay(values, fps, endTime);
    }

    internal void Apply(int frame, DesktopInput input)
    {
        while (next < events.Count && events[next].Frame <= frame)
        {
            var e = events[next++].Value;
            switch (e.Type)
            {
                case "move": input.PointerMove(e.X, e.Y); break;
                case "down": input.PointerDown(e.X, e.Y, e.Button); break;
                case "up": input.PointerUp(e.X, e.Y, e.Button); break;
                case "type": input.TextInput(e.Text!); break;
                case "scroll": input.PointerMove(e.X, e.Y); input.Scroll(e.DeltaX, e.DeltaY); break;
                case "keyDown": input.KeyDown(Enum.Parse<Keys>(e.Key!, true)); break;
                case "keyUp": input.KeyUp(Enum.Parse<Keys>(e.Key!, true)); break;
            }
        }
    }
}
