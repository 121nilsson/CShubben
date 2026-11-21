using System.Globalization;
using System.Text.RegularExpressions;

namespace Cshubben.Core.Shared;

public readonly record struct SvgPoint(double X, double Y);

public enum SvgSegmentKind { Line, Arc, Cubic }

/// <summary>One drawing step. For arcs <see cref="Sweep"/> true means clockwise (SVG sweep-flag 1).</summary>
public sealed record SvgSegment(
    SvgSegmentKind Kind,
    SvgPoint To,
    SvgPoint Control1 = default,
    SvgPoint Control2 = default,
    double RadiusX = 0,
    double RadiusY = 0,
    double Rotation = 0,
    bool LargeArc = false,
    bool Sweep = false);

public sealed record SvgFigure(SvgPoint Start, IReadOnlyList<SvgSegment> Segments, bool Closed);

/// <summary>
/// A small SVG path reader for icons: M L H V C S A and Z in absolute and relative form. It turns the text into plain figures
/// so the UI can build real geometry from them instead of depending on a text parser (which choked on icon data).
/// </summary>
public static class SvgPath
{
    private static readonly Regex Token = new(@"[A-Za-z]|-?(?:\d+\.\d*|\.\d+|\d+)", RegexOptions.CultureInvariant);

    public static IReadOnlyList<SvgFigure> Parse(string data)
    {
        ArgumentNullException.ThrowIfNull(data);
        var tokens = Token.Matches(data).Select(m => m.Value).ToList();
        var figures = new List<SvgFigure>();
        var segments = new List<SvgSegment>();
        SvgPoint start = default, current = default, lastControl = default;
        var haveFigure = false;
        char command = ' ', previous = ' ';
        var i = 0;

        double Num()
        {
            if (i >= tokens.Count || char.IsLetter(tokens[i][0])) throw new FormatException("Missing number in path data near token " + i + ".");
            return double.Parse(tokens[i++], CultureInfo.InvariantCulture);
        }

        bool Flag() => Num() != 0;

        void Finish(bool closed)
        {
            if (haveFigure && segments.Count > 0) figures.Add(new SvgFigure(start, segments.ToArray(), closed));
            segments = new List<SvgSegment>();
            haveFigure = false;
        }

        void Begin()
        {
            if (haveFigure) return;
            start = current;
            haveFigure = true;
        }

        while (i < tokens.Count)
        {
            if (char.IsLetter(tokens[i][0])) command = tokens[i++][0];
            else if (command is 'Z' or 'z' or ' ') throw new FormatException("Unexpected number in path data near token " + i + ".");
            else if (command == 'M') command = 'L';
            else if (command == 'm') command = 'l';

            var relative = char.IsLower(command);
            var ox = relative ? current.X : 0;
            var oy = relative ? current.Y : 0;

            switch (char.ToUpperInvariant(command))
            {
                case 'M':
                    Finish(false);
                    current = new SvgPoint(ox + Num(), oy + Num());
                    start = current;
                    break;
                case 'L':
                    Begin();
                    current = new SvgPoint(ox + Num(), oy + Num());
                    segments.Add(new SvgSegment(SvgSegmentKind.Line, current));
                    break;
                case 'H':
                    Begin();
                    current = new SvgPoint(ox + Num(), current.Y);
                    segments.Add(new SvgSegment(SvgSegmentKind.Line, current));
                    break;
                case 'V':
                    Begin();
                    current = new SvgPoint(current.X, oy + Num());
                    segments.Add(new SvgSegment(SvgSegmentKind.Line, current));
                    break;
                case 'C':
                {
                    Begin();
                    var c1 = new SvgPoint(ox + Num(), oy + Num());
                    var c2 = new SvgPoint(ox + Num(), oy + Num());
                    var to = new SvgPoint(ox + Num(), oy + Num());
                    segments.Add(new SvgSegment(SvgSegmentKind.Cubic, to, c1, c2));
                    lastControl = c2;
                    current = to;
                    break;
                }
                case 'S':
                {
                    Begin();
                    var c1 = previous is 'C' or 'c' or 'S' or 's'
                        ? new SvgPoint(2 * current.X - lastControl.X, 2 * current.Y - lastControl.Y)
                        : current;
                    var c2 = new SvgPoint(ox + Num(), oy + Num());
                    var to = new SvgPoint(ox + Num(), oy + Num());
                    segments.Add(new SvgSegment(SvgSegmentKind.Cubic, to, c1, c2));
                    lastControl = c2;
                    current = to;
                    break;
                }
                case 'A':
                {
                    Begin();
                    var rx = Num();
                    var ry = Num();
                    var rotation = Num();
                    var large = Flag();
                    var sweep = Flag();
                    var to = new SvgPoint(ox + Num(), oy + Num());
                    segments.Add(new SvgSegment(SvgSegmentKind.Arc, to, default, default, Math.Abs(rx), Math.Abs(ry), rotation, large, sweep));
                    current = to;
                    break;
                }
                case 'Z':
                    Finish(true);
                    current = start;
                    break;
                default:
                    throw new FormatException("Unsupported path command '" + command + "'.");
            }

            previous = command;
        }

        Finish(false);
        return figures;
    }
}
