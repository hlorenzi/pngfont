using System.Collections.Generic;
using System.IO;
using System;
using System.Threading.Tasks;
using System.Drawing;


namespace pngfont
{
    class Program
    {
        static void Main(string[] args)
        {
            //args = new string[] { "../../test_kanji.png", "--kanji" };

            if (args.Length > 1 && args[1] == "--kanji")
                HandleKanji(args);
            else
                HandleStandard(args);

            Console.Out.WriteLine("success");
        }


        static void HandleStandard(string[] args)
        {
            var sourceFilename = args[0];
            var sourceBitmap = new Bitmap(sourceFilename);

            var glyphs = new List<Glyph>();
            var currentUnicode = 0u;

            foreach (var yBase in DetectRowBases(sourceBitmap))
            {
                var rowGuides = DetectRowGuides(sourceBitmap, yBase);
                uint rowStartUnicode;
                if (!DetectRowStartUnicode(sourceBitmap, rowGuides, out rowStartUnicode))
                    rowStartUnicode = currentUnicode;

                Console.Out.WriteLine("row U+" + rowStartUnicode.ToString("x04"));

                currentUnicode = rowStartUnicode;
                foreach (var glyphGuides in DetectGlyphGuides(sourceBitmap, rowGuides))
                {
                    var glyph = DetectGlyph(sourceBitmap, currentUnicode, rowGuides, glyphGuides);

                    Console.Out.WriteLine(
                        "glyph " +
                        "U+" + currentUnicode.ToString("x04") + " " +
                        "(bounds: (" + glyph.xMin + ", " + glyph.yMin + ") to (" + glyph.xMax + ", " + glyph.yMax + ")) " +
                        "(x-align: " + glyph.xAlignMin + " to " + glyph.xAlignMax + ") " +
                        "(y-base: " + glyph.yBase + ")");

                    glyphs.Add(glyph);
                    currentUnicode++;
                }
            }

            var outputFilename = Path.ChangeExtension(sourceFilename, ".sprsheet");
            WriteSpriteSheet(Path.GetFileName(sourceFilename), outputFilename, glyphs);
        }


        static void HandleKanji(string[] args)
        {
            var sourceFilename = args[0];
            var sourceBitmap = new Bitmap(sourceFilename);

            var unicodeOffset = 0u;
            if (args.Length > 2)
                unicodeOffset = Convert.ToUInt32(args[2], 16);

            var charsFilename = Path.ChangeExtension(args[0], ".txt");
            var charsStr = File.ReadAllText(charsFilename);
            var chars = new List<uint>();

            for (int i = 0; i < charsStr.Length; i++)
            {
                int unicodeCodePoint = char.ConvertToUtf32(charsStr, i);
                if (unicodeCodePoint > 0xffff)
                    i++;

                chars.Add((uint)unicodeCodePoint);
            }

            var guide = DetectFirstColumnWith(sourceBitmap, 0, sourceBitmap.Width, 0, sourceBitmap.Height, IsRedGuideColor).Value;
            var guideW = DetectLastContiguousColumnWith(sourceBitmap, guide.x, sourceBitmap.Width, guide.y, guide.y + 1, IsRedGuideColor) - guide.x;

            Console.Out.WriteLine("kanji unicode offset(U+" + unicodeOffset.ToString("4X") + ")");
            Console.Out.WriteLine("kanji guide width(" + guideW + ")");

            var glyphs = new List<Glyph>();
            var index = 0;

            var y = 0;

            while (true)
            {
                var x = 0;

                var glyphPointDummy = DetectFirstRowAndColumnWith(sourceBitmap, x, y, IsGlyphColorAndNotRedGuide);
                if (glyphPointDummy == null)
                    break;

                var lineYMax = DetectLastContiguousRowWith(sourceBitmap, x, sourceBitmap.Width, glyphPointDummy.Value.y, sourceBitmap.Height, IsGlyphColorAndNotRedGuide);
                Console.WriteLine("line yMax(" + lineYMax + ")");

                while (true)
                {
                    var glyphPoint = DetectFirstRowAndColumnWith(sourceBitmap, x, y, IsGlyphColorAndNotRedGuide);
                    if (glyphPoint == null)
                        break;

                    var maybeGlyph = DetectGlyphRect(sourceBitmap, glyphPoint.Value.x, glyphPoint.Value.x + guideW, y, lineYMax);
                    if (!maybeGlyph.HasValue)
                        break;

                    var glyph = maybeGlyph.Value;
                    glyph.unicode = unicodeOffset + (index >= chars.Count ? 0 : chars[index]);
                    index++;

                    x = glyph.xMax + 1;

                    glyph.xAlignMin = glyph.xMin + (glyph.xMax - glyph.xMin) / 2 - guideW / 2;
                    glyph.xAlignMax = glyph.xMin + (glyph.xMax - glyph.xMin) / 2 + guideW / 2;
                    glyph.yBase = lineYMax;

                    Console.Out.WriteLine(
                        "glyph " +
                        "U+" + glyph.unicode.ToString("x04") + " " +
                        "(bounds: (" + glyph.xMin + ", " + glyph.yMin + ") to (" + glyph.xMax + ", " + glyph.yMax + ")) " +
                        "(x-align: " + glyph.xAlignMin + " to " + glyph.xAlignMax + ") " +
                        "(y-base: " + glyph.yBase + ")");

                    glyphs.Add(glyph);
                }

                y = lineYMax + 1;
            }

            var outputFilename = Path.ChangeExtension(sourceFilename, ".sprsheet");
            WriteSpriteSheet(Path.GetFileName(sourceFilename), outputFilename, glyphs);

            if (index > chars.Count)
            {
                Console.WriteLine("mismatch between .png glyphs and .txt characters");
                Console.ReadKey();
            }
        }


        static bool IsRedGuideColor(Color c)
        {
            return c.R > 250 && c.G < 5 && c.B < 5 && c.A > 250;
        }


        static bool IsDigitColor(Color c)
        {
            return c.A > 250 && !IsRedGuideColor(c);
        }


        static bool IsGlyphColor(Color c)
        {
            return c.A > 0;
        }


        static bool IsGlyphColorAndNotRedGuide(Color c)
        {
            return IsGlyphColor(c) && !IsRedGuideColor(c);
        }


        static Color GetPixel(Bitmap img, int x, int y)
        {
            if (x < 0 || y < 0 || x >= img.Width || y >= img.Height)
                return Color.FromArgb(0, 0, 0, 0);
            else
                return img.GetPixel(x, y);
        }


        struct Point
        {
            public int x, y;
        }


        static Point? DetectFirstRowAndColumnWith(Bitmap img, int xMin, int yMin, System.Func<Color, bool> filterFn)
        {
            for (var diagX = 0; diagX < Math.Max(img.Width, img.Height); diagX++)
            {
                for (var diagY = 0; diagY <= diagX; diagY++)
                {
                    var x = xMin + diagX - diagY;
                    var y = yMin + diagY;

                    if (filterFn(GetPixel(img, x, y)))
                        return new Point { x = x, y = y };
                }
            }

            return null;
        }


        static Point? DetectFirstColumnWith(Bitmap img, int xMin, int xMax, int yMin, int yMax, System.Func<Color, bool> filterFn)
        {
            for (var x = xMin; x <= xMax; x++)
            {
                for (var y = yMin; y <= yMax; y++)
                {
                    if (filterFn(GetPixel(img, x, y)))
                        return new Point { x = x, y = y };
                }
            }

            return null;
        }


        static int DetectLastContiguousColumnWith(Bitmap img, int xMin, int xMax, int yMin, int yMax, System.Func<Color, bool> filterFn)
        {
            for (var x = xMin; x <= xMax; x++)
            {
                var found = false;

                for (var y = yMin; y <= yMax; y++)
                {
                    if (filterFn(GetPixel(img, x, y)))
                        found = true;
                }

                if (!found)
                    return x;
            }

            return xMax;
        }


        static int DetectLastContiguousRowWith(Bitmap img, int xMin, int xMax, int yMin, int yMax, System.Func<Color, bool> filterFn)
        {
            for (var y = yMin; y <= yMax; y++)
            {
                var found = false;

                for (var x = xMin; x <= xMax; x++)
                {
                    if (filterFn(GetPixel(img, x, y)))
                        found = true;
                }

                if (!found)
                    return y;
            }

            return yMax;
        }


        static void TraverseConnected(Bitmap img, int xImg, int yImg, HashSet<Point> traversed, System.Func<Color, bool> filterFn, System.Action<int, int> callback)
        {
            var point = new Point { x = xImg, y = yImg };

            if (!traversed.Contains(point) && filterFn(GetPixel(img, xImg, yImg)))
            {
                callback(xImg, yImg);

                traversed.Add(point);

                TraverseConnected(img, xImg - 1, yImg, traversed, filterFn, callback);
                TraverseConnected(img, xImg + 1, yImg, traversed, filterFn, callback);
                TraverseConnected(img, xImg, yImg - 1, traversed, filterFn, callback);
                TraverseConnected(img, xImg, yImg + 1, traversed, filterFn, callback);
            }
        }


        static IEnumerable<int> DetectRowBases(Bitmap img)
        {
            for (var y = 0; y < img.Height; y++)
            {
                if (IsRedGuideColor(GetPixel(img, 0, y)))
                    yield return y;
            }
        }


        struct RowGuides
        {
            public int xDigitAreaMax, xBaseMax;
            public int yBase, yMin, yMax;
        }


        static RowGuides DetectRowGuides(Bitmap img, int yBase)
        {
            var x = 0;

            while (true)
            {
                if (!IsRedGuideColor(GetPixel(img, x, yBase)))
                    throw new System.Exception("missing row top/bottom guides");

                if (IsRedGuideColor(GetPixel(img, x, yBase - 1)) && IsRedGuideColor(GetPixel(img, x, yBase + 1)))
                    break;

                x++;
            }

            var xDigitAreaMax = x;
            var yMin = yBase - 1;
            var yMax = yBase + 1;

            while (IsRedGuideColor(GetPixel(img, x, yMin)))
                yMin--;

            while (IsRedGuideColor(GetPixel(img, x, yMax)))
                yMax++;

            while (IsRedGuideColor(GetPixel(img, x, yBase)))
                x++;

            return new RowGuides { xDigitAreaMax = xDigitAreaMax, xBaseMax = x, yBase = yBase, yMin = yMin, yMax = yMax };
        }


        static bool DetectRowStartUnicode(Bitmap img, RowGuides rowGuides, out uint unicode)
        {
            unicode = 0u;
            var x = 0;

            var hasNumber = false;

            while (true)
            {
                var digitPointMin = DetectFirstColumnWith(img, x, rowGuides.xDigitAreaMax, rowGuides.yMin, rowGuides.yBase, IsDigitColor);
                if (!digitPointMin.HasValue)
                    break;

                hasNumber = true;
                x = DetectLastContiguousColumnWith(img, digitPointMin.Value.x, rowGuides.xDigitAreaMax, rowGuides.yMin, rowGuides.yBase, IsDigitColor);

                var digitValue = DetectDigitValue(img, digitPointMin.Value);
                unicode = (unicode * 16) + digitValue;
            }

            return hasNumber;
        }


        static uint DetectDigitValue(Bitmap img, Point startingPixel)
        {
            var pixels = new bool[3, 7];
            var yMin = 7;

            TraverseConnected(img, startingPixel.x, startingPixel.y, new HashSet<Point>(), IsDigitColor, (x, y) =>
            {
                x += -startingPixel.x;
                y += -startingPixel.y + 2;

                if (x < 0 || y < 0 || x >= 3 || y >= 7)
                    return;

                if (y < yMin)
                    yMin = y;

                pixels[x, y] = true;
            });

            System.Func<int, int, bool> yes = (x, y) => pixels[x, y + yMin];
            System.Func<int, int, bool> not = (x, y) => !pixels[x, y + yMin];

            var digit0 = yes(0, 1) && yes(0, 3) && yes(1, 0) && not(1, 2) && yes(1, 4) && yes(2, 1) && yes(2, 3);
            var digit1 = yes(0, 1) && yes(0, 3) && not(1, 0) && not(1, 2) && not(1, 4) && not(2, 1) && not(2, 3);
            var digit2 = not(0, 1) && yes(0, 3) && yes(1, 0) && yes(1, 2) && yes(1, 4) && yes(2, 1) && not(2, 3);
            var digit3 = not(0, 1) && not(0, 3) && yes(1, 0) && yes(1, 2) && yes(1, 4) && yes(2, 1) && yes(2, 3);
            var digit4 = yes(0, 1) && not(0, 3) && not(1, 0) && yes(1, 2) && not(1, 4) && yes(2, 1) && yes(2, 3);
            var digit5 = yes(0, 1) && not(0, 3) && yes(1, 0) && yes(1, 2) && yes(1, 4) && not(2, 1) && yes(2, 3);
            var digit6 = yes(0, 1) && yes(0, 3) && yes(1, 0) && yes(1, 2) && yes(1, 4) && not(2, 1) && yes(2, 3);
            var digit7 = not(0, 1) && not(0, 3) && yes(1, 0) && not(1, 2) && not(1, 4) && yes(2, 1) && yes(2, 3);
            var digit8 = yes(0, 1) && yes(0, 3) && yes(1, 0) && yes(1, 2) && yes(1, 4) && yes(2, 1) && yes(2, 3);
            var digit9 = yes(0, 1) && not(0, 3) && yes(1, 0) && yes(1, 2) && yes(1, 4) && yes(2, 1) && yes(2, 3);
            var digitA = yes(0, 1) && yes(0, 3) && yes(1, 0) && yes(1, 2) && not(1, 4) && yes(2, 1) && yes(2, 3);
            var digitB = yes(0, 1) && yes(0, 3) && not(1, 0) && yes(1, 2) && yes(1, 4) && not(2, 1) && yes(2, 3);
            var digitC = yes(0, 1) && yes(0, 3) && yes(1, 0) && not(1, 2) && yes(1, 4) && not(2, 1) && not(2, 3);
            var digitD = not(0, 1) && yes(0, 3) && not(1, 0) && yes(1, 2) && yes(1, 4) && yes(2, 1) && yes(2, 3);
            var digitE = yes(0, 1) && yes(0, 3) && yes(1, 0) && yes(1, 2) && yes(1, 4) && not(2, 1) && not(2, 3);
            var digitF = yes(0, 1) && yes(0, 3) && yes(1, 0) && yes(1, 2) && not(1, 4) && not(2, 1) && not(2, 3);

            if (digit0) return 0;
            if (digit1) return 1;
            if (digit2) return 2;
            if (digit3) return 3;
            if (digit4) return 4;
            if (digit5) return 5;
            if (digit6) return 6;
            if (digit7) return 7;
            if (digit8) return 8;
            if (digit9) return 9;
            if (digitA) return 10;
            if (digitB) return 11;
            if (digitC) return 12;
            if (digitD) return 13;
            if (digitE) return 14;
            if (digitF) return 15;

            throw new System.Exception("could not identify digit");
        }


        struct GlyphGuides
        {
            public int yBase;
            public int xMin, xMax;
            public int xAlignMin, xAlignMax;
        }


        static IEnumerable<GlyphGuides> DetectGlyphGuides(Bitmap img, RowGuides rowGuides)
        {
            var x = rowGuides.xBaseMax;

            while (true)
            {
                var glyphBaseGuide = DetectFirstColumnWith(img, x, img.Width, rowGuides.yMin, rowGuides.yMax, IsRedGuideColor);
                if (!glyphBaseGuide.HasValue)
                    break;

                x = glyphBaseGuide.Value.x;
                while (IsRedGuideColor(img.GetPixel(x, glyphBaseGuide.Value.y)))
                    x++;

                yield return new GlyphGuides
                {
                    yBase = glyphBaseGuide.Value.y,
                    xMin = glyphBaseGuide.Value.x,
                    xMax = x,
                    xAlignMin = glyphBaseGuide.Value.x,
                    xAlignMax = x
                };
            }
        }


        struct Glyph
        {
            public uint unicode;
            public int xMin, xMax, yMin, yMax;
            public int xAlignMin, xAlignMax;
            public int yBase;
        }


        static Glyph DetectGlyph(Bitmap img, uint unicode, RowGuides rowGuides, GlyphGuides glyphGuides)
        {
            var xMin = glyphGuides.xMin;
            var xMax = glyphGuides.xMax;
            var yMin = glyphGuides.yBase;
            var yMax = glyphGuides.yBase;

            var traversedPoints = new HashSet<Point>();

            for (var x = glyphGuides.xMin; x < glyphGuides.xMax; x++)
            {
                for (var y = rowGuides.yMin; y < glyphGuides.yBase; y++)
                {
                    TraverseConnected(img, x, y, traversedPoints, IsGlyphColor, (xTraversed, yTraversed) =>
                    {
                        xMin = Math.Min(xMin, xTraversed);
                        xMax = Math.Max(xMax, xTraversed + 1);
                        yMin = Math.Min(yMin, yTraversed);
                        yMax = Math.Max(yMax, yTraversed + 1);
                    });
                }
            }

            return new Glyph
            {
                unicode = unicode,
                xMin = xMin,
                xMax = xMax,
                yMin = yMin,
                yMax = yMax,
                xAlignMin = glyphGuides.xAlignMin,
                xAlignMax = glyphGuides.xAlignMax,
                yBase = rowGuides.yBase
            };
        }


        static Glyph? DetectGlyphRect(Bitmap img, int xMin, int xMax, int yMin, int yMax)
        {
            var xMinGlyph = img.Width;
            var xMaxGlyph = 0;
            var yMinGlyph = img.Height;
            var yMaxGlyph = 0;

            var traversedPoints = new HashSet<Point>();

            for (var x = xMin; x < xMax; x++)
            {
                for (var y = yMin; y < yMax; y++)
                {
                    TraverseConnected(img, x, y, traversedPoints, IsGlyphColor, (xTraversed, yTraversed) =>
                    {
                        xMinGlyph = Math.Min(xMinGlyph, xTraversed);
                        xMaxGlyph = Math.Max(xMaxGlyph, xTraversed + 1);
                        yMinGlyph = Math.Min(yMinGlyph, yTraversed);
                        yMaxGlyph = Math.Max(yMaxGlyph, yTraversed + 1);
                    });
                }
            }

            if (xMinGlyph > xMaxGlyph || yMinGlyph > yMaxGlyph)
                return null;

            return new Glyph
            {
                xMin = xMinGlyph,
                xMax = xMaxGlyph,
                yMin = yMinGlyph,
                yMax = yMaxGlyph,
            };
        }


        static void WriteSpriteSheet(string sourceFilename, string outputFilename, List<Glyph> glyphs)
        {
            using (var file = File.CreateText(outputFilename))
            {
                file.Write("<sprite-sheet src=\"" + sourceFilename + "\">\n");

                foreach (var glyph in glyphs)
                {
                    file.Write(
                        "\t<sprite name=\"" + glyph.unicode.ToString("x") + "\" " +
                        "x=\"" + glyph.xMin + "\" " +
                        "y=\"" + glyph.yMin + "\" " +
                        "width=\"" + (glyph.xMax - glyph.xMin) + "\" " +
                        "height=\"" + (glyph.yMax - glyph.yMin) + "\">\n");

                    file.Write(
                        "\t\t<guide name=\"base-advance\" " +
                        "kind=\"vector\" " +
                        "x1=\"" + (glyph.xAlignMin - glyph.xMin) + "\" " +
                        "y1=\"" + (glyph.yBase - glyph.yMin) + "\" " +
                        "x2=\"" + (glyph.xAlignMax - glyph.xMin) + "\" " +
                        "y2=\"" + (glyph.yBase - glyph.yMin) + "\"></guide>\n");

                    file.Write("\t</sprite>\n");
                }

                file.Write("</sprite-sheet>");
            }
        }
    }
}
