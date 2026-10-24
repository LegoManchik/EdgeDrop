using System;
using System.Collections.Generic;
using System.Text;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace EdgeDrop;

public class DataTooLongException : Exception
{
    public DataTooLongException() { }

    public DataTooLongException(string message) : base(message) { }
}

public sealed class BitBuffer
{
    private readonly List<bool> _data = new();
    public int BitLength => _data.Count;

    public int GetBit(int index)
    {
        if (index < 0 || index >= _data.Count)

            throw new IndexOutOfRangeException();

        return _data[index] ? 1 : 0;
    }

    public void AppendBits(int val, int len)
    {
        if (len < 0 || len > 31 || (val >>> len) != 0)

            throw new ArgumentOutOfRangeException(nameof(len));

        for (int i = len - 1; i >= 0; i--)

            _data.Add(((val >> i) & 1) != 0);
    }

    public void AppendData(BitBuffer bb)
    {
        ArgumentNullException.ThrowIfNull(bb);

        _data.AddRange(bb._data);
    }

    public BitBuffer Clone()
    {
        var copy = new BitBuffer();

        copy._data.AddRange(_data);

        return copy;
    }
}

public sealed class QrSegment
{
    public sealed class Mode
    {
        public static readonly Mode NUMERIC = new Mode(0x1, 10, 12, 14);
        public static readonly Mode ALPHANUMERIC = new Mode(0x2, 9, 11, 13);
        public static readonly Mode BYTE = new Mode(0x4, 8, 16, 16);
        public static readonly Mode KANJI = new Mode(0x8, 8, 10, 12);
        public static readonly Mode ECI = new Mode(0x7, 0, 0, 0);
        internal readonly int ModeBits;
        private readonly int[] NumBitsCharCount;

        private Mode(int mode, params int[] ccbits)
        {
            ModeBits = mode;

            NumBitsCharCount = ccbits;
        }

        internal int NumCharCountBits(int ver)
        {
            return NumBitsCharCount[(ver + 7) / 17];
        }
    }

    public Mode mode { get; }

    public int numChars { get; }

    internal BitBuffer data { get; }

    public QrSegment(Mode md, int numCh, BitBuffer data)
    {
        mode = md ?? throw new ArgumentNullException(nameof(md));

        this.data = (data ?? throw new ArgumentNullException(nameof(data))).Clone();

        if (numCh < 0)

            throw new ArgumentOutOfRangeException(nameof(numCh));

        numChars = numCh;
    }

    public static QrSegment MakeBytes(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);

        var bb = new BitBuffer();

        foreach (byte b in data)

            bb.AppendBits(b, 8);

        return new QrSegment(Mode.BYTE, data.Length, bb);
    }

    public static QrSegment MakeNumeric(string digits)
    {
        ArgumentNullException.ThrowIfNull(digits);

        if (!IsNumeric(digits))

            throw new ArgumentException("String contains non-numeric characters");

        var bb = new BitBuffer();

        for (int i = 0; i < digits.Length;)
        {
            int n = Math.Min(digits.Length - i, 3);

            bb.AppendBits(int.Parse(digits.Substring(i, n)), n * 3 + 1);

            i += n;
        }

        return new QrSegment(Mode.NUMERIC, digits.Length, bb);
    }

    public static QrSegment MakeAlphanumeric(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        if (!IsAlphanumeric(text))

            throw new ArgumentException("String contains unencodable characters in alphanumeric mode");

        var bb = new BitBuffer();

        int i;

        for (i = 0; i <= text.Length - 2; i += 2)
        {
            int temp = ALPHANUMERIC_CHARSET.IndexOf(text[i]) * 45;

            temp += ALPHANUMERIC_CHARSET.IndexOf(text[i + 1]);

            bb.AppendBits(temp, 11);
        }

        if (i < text.Length)

            bb.AppendBits(ALPHANUMERIC_CHARSET.IndexOf(text[i]), 6);

        return new QrSegment(Mode.ALPHANUMERIC, text.Length, bb);
    }

    public static List<QrSegment> MakeSegments(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var result = new List<QrSegment>();

        if (text.Length == 0)

            return result;

        if (IsNumeric(text))

            result.Add(MakeNumeric(text));

        else if (IsAlphanumeric(text))

            result.Add(MakeAlphanumeric(text));

        else

            result.Add(MakeBytes(Encoding.UTF8.GetBytes(text)));

        return result;
    }

    public static bool IsNumeric(string text)
    {
        foreach (char c in text)
        {
            if (c < '0' || c > '9')

                return false;
        }

        return true;
    }

    public static bool IsAlphanumeric(string text)
    {
        foreach (char c in text)
        {
            if (ALPHANUMERIC_CHARSET.IndexOf(c) < 0)

                return false;
        }

        return true;
    }

    internal static int GetTotalBits(List<QrSegment> segs, int version)
    {
        ArgumentNullException.ThrowIfNull(segs);

        long result = 0;

        foreach (var seg in segs)
        {
            ArgumentNullException.ThrowIfNull(seg);

            int ccbits = seg.mode.NumCharCountBits(version);

            if (seg.numChars >= (1 << ccbits))

                return -1;

            result += 4L + ccbits + seg.data.BitLength;

            if (result > int.MaxValue)

                return -1;
        }

        return (int)result;
    }

    private const string ALPHANUMERIC_CHARSET = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ $%*+-./:";
}

public sealed class QrCode
{
    public sealed class Ecc
    {
        public static readonly Ecc LOW = new Ecc(0, 1);
        public static readonly Ecc MEDIUM = new Ecc(1, 0);
        public static readonly Ecc QUARTILE = new Ecc(2, 3);
        public static readonly Ecc HIGH = new Ecc(3, 2);

        public static readonly Ecc[] Values = { LOW, MEDIUM, QUARTILE, HIGH };

        public int Ordinal { get; }

        internal int FormatBits { get; }

        private Ecc(int ordinal, int fb)
        {
            Ordinal = ordinal;

            FormatBits = fb;
        }
    }

    public static QrCode EncodeText(string text, Ecc ecl)
    {
        ArgumentNullException.ThrowIfNull(text);

        ArgumentNullException.ThrowIfNull(ecl);

        List<QrSegment> segs = QrSegment.MakeSegments(text);

        return EncodeSegments(segs, ecl);
    }

    public static QrCode EncodeBinary(byte[] data, Ecc ecl)
    {
        ArgumentNullException.ThrowIfNull(data);

        ArgumentNullException.ThrowIfNull(ecl);

        QrSegment seg = QrSegment.MakeBytes(data);

        return EncodeSegments(new List<QrSegment> { seg }, ecl);
    }

    public static QrCode EncodeSegments(List<QrSegment> segs, Ecc ecl)
    {
        return EncodeSegments(segs, ecl, MIN_VERSION, MAX_VERSION, -1, true);
    }

    public static QrCode EncodeSegments(List<QrSegment> segs, Ecc ecl, int minVersion, int maxVersion, int mask, bool boostEcl)
    {
        ArgumentNullException.ThrowIfNull(segs);

        ArgumentNullException.ThrowIfNull(ecl);

        if (!(MIN_VERSION <= minVersion && minVersion <= maxVersion && maxVersion <= MAX_VERSION) || mask < -1 || mask > 7)

            throw new ArgumentOutOfRangeException("Invalid min/max version or mask");

        int version, dataUsedBits;

        for (version = minVersion; ; version++)
        {
            int dataCapacityBits = GetNumDataCodewords(version, ecl) * 8;

            dataUsedBits = QrSegment.GetTotalBits(segs, version);

            if (dataUsedBits != -1 && dataUsedBits <= dataCapacityBits)

                break;

            if (version >= maxVersion)
            {
                string msg = "Segment too long";

                if (dataUsedBits != -1)

                    msg = $"Data length = {dataUsedBits} bits, Max capacity = {dataCapacityBits} bits";

                throw new DataTooLongException(msg);
            }
        }

        if (boostEcl)
        {
            foreach (Ecc newEcl in Ecc.Values)
            {
                if (dataUsedBits <= GetNumDataCodewords(version, newEcl) * 8)

                    ecl = newEcl;
            }
        }

        var bb = new BitBuffer();

        foreach (QrSegment seg in segs)
        {
            bb.AppendBits(seg.mode.ModeBits, 4);

            bb.AppendBits(seg.numChars, seg.mode.NumCharCountBits(version));

            bb.AppendData(seg.data);
        }

        int capacityBits = GetNumDataCodewords(version, ecl) * 8;

        bb.AppendBits(0, Math.Min(4, capacityBits - bb.BitLength));

        bb.AppendBits(0, (8 - bb.BitLength % 8) % 8);

        for (int padByte = 0xEC; bb.BitLength < capacityBits; padByte ^= 0xEC ^ 0x11)

            bb.AppendBits(padByte, 8);

        byte[] dataCodewords = new byte[bb.BitLength / 8];

        for (int i = 0; i < bb.BitLength; i++)

            dataCodewords[i >>> 3] |= (byte)(bb.GetBit(i) << (7 - (i & 7)));

        return new QrCode(version, ecl, dataCodewords, mask);
    }

    public int Version { get; }

    public int Size { get; }

    public Ecc ErrorCorrectionLevel { get; }

    public int Mask { get; }

    private readonly bool[][] _modules;

    public QrCode(int ver, Ecc ecl, byte[] dataCodewords, int msk)
    {
        if (ver < MIN_VERSION || ver > MAX_VERSION)

            throw new ArgumentOutOfRangeException(nameof(ver));

        if (msk < -1 || msk > 7)

            throw new ArgumentOutOfRangeException(nameof(msk));

        Version = ver;

        Size = ver * 4 + 17;

        ErrorCorrectionLevel = ecl ?? throw new ArgumentNullException(nameof(ecl));

        ArgumentNullException.ThrowIfNull(dataCodewords);

        _modules = new bool[Size][];

        var isFunction = new bool[Size][];

        for (int i = 0; i < Size; i++)
        {
            _modules[i] = new bool[Size];

            isFunction[i] = new bool[Size];
        }

        DrawFunctionPatterns(isFunction);

        byte[] allCodewords = AddEccAndInterleave(dataCodewords);

        DrawCodewords(allCodewords, isFunction);

        if (msk == -1)
        {
            int minPenalty = int.MaxValue;

            for (int i = 0; i < 8; i++)
            {
                ApplyMask(i, isFunction);

                DrawFormatBits(i, isFunction);

                int penalty = GetPenaltyScore();

                if (penalty < minPenalty)
                {
                    msk = i;

                    minPenalty = penalty;
                }

                ApplyMask(i, isFunction);
            }
        }

        Mask = msk;

        ApplyMask(msk, isFunction);

        DrawFormatBits(msk, isFunction);
    }

    public bool GetModule(int x, int y)
    {
        return 0 <= x && x < Size && 0 <= y && y < Size && _modules[y][x];
    }

    public BitmapSource ToBitmapSource(int scale = 4, int border = 4)
    {
        int fullSize = (Size + border * 2) * scale;

        int stride = fullSize * 4;

        byte[] pixels = new byte[stride * fullSize];

        Array.Fill(pixels, (byte)255);

        for (int y = 0; y < Size; y++)
        {
            for (int x = 0; x < Size; x++)
            {
                if (GetModule(x, y))
                {
                    int startX = (x + border) * scale;

                    int startY = (y + border) * scale;

                    for (int dy = 0; dy < scale; dy++)
                    {
                        int rowOffset = (startY + dy) * stride;

                        for (int dx = 0; dx < scale; dx++)
                        {
                            int p = rowOffset + (startX + dx) * 4;

                            pixels[p] = 0;

                            pixels[p + 1] = 0;

                            pixels[p + 2] = 0;

                            pixels[p + 3] = 255;
                        }
                    }
                }
            }
        }

        var bmp = BitmapSource.Create(

            fullSize, fullSize, 96, 96,
            PixelFormats.Bgr32, null,

            pixels, stride);

        bmp.Freeze();

        return bmp;
    }

    private void DrawFunctionPatterns(bool[][] isFunction)
    {
        for (int i = 0; i < Size; i++)
        {
            SetFunctionModule(6, i, i % 2 == 0, isFunction);

            SetFunctionModule(i, 6, i % 2 == 0, isFunction);
        }

        DrawFinderPattern(3, 3, isFunction);

        DrawFinderPattern(Size - 4, 3, isFunction);

        DrawFinderPattern(3, Size - 4, isFunction);

        int[] alignPatPos = GetAlignmentPatternPositions();

        int numAlign = alignPatPos.Length;

        for (int i = 0; i < numAlign; i++)
        {
            for (int j = 0; j < numAlign; j++)
            {
                if (!(i == 0 && j == 0 || i == 0 && j == numAlign - 1 || i == numAlign - 1 && j == 0))

                    DrawAlignmentPattern(alignPatPos[i], alignPatPos[j], isFunction);
            }
        }

        DrawFormatBits(0, isFunction);

        DrawVersion(isFunction);
    }

    private void DrawFormatBits(int msk, bool[][]? isFunction)
    {
        int data = ErrorCorrectionLevel.FormatBits << 3 | msk;

        int rem = data;

        for (int i = 0; i < 10; i++)

            rem = (rem << 1) ^ ((rem >>> 9) * 0x537);

        int bits = (data << 10 | rem) ^ 0x5412;

        for (int i = 0; i <= 5; i++)

            SetFunctionModule(8, i, GetBit(bits, i), isFunction);

        SetFunctionModule(8, 7, GetBit(bits, 6), isFunction);

        SetFunctionModule(8, 8, GetBit(bits, 7), isFunction);

        SetFunctionModule(7, 8, GetBit(bits, 8), isFunction);

        for (int i = 9; i < 15; i++)

            SetFunctionModule(14 - i, 8, GetBit(bits, i), isFunction);

        for (int i = 0; i < 8; i++)

            SetFunctionModule(Size - 1 - i, 8, GetBit(bits, i), isFunction);

        for (int i = 8; i < 15; i++)

            SetFunctionModule(8, Size - 15 + i, GetBit(bits, i), isFunction);

        SetFunctionModule(8, Size - 8, true, isFunction);
    }

    private void DrawVersion(bool[][] isFunction)
    {
        if (Version < 7)

            return;

        int rem = Version;

        for (int i = 0; i < 12; i++)

            rem = (rem << 1) ^ ((rem >>> 11) * 0x1F25);

        int bits = Version << 12 | rem;

        for (int i = 0; i < 18; i++)
        {
            bool bit = GetBit(bits, i);

            int a = Size - 11 + i % 3;

            int b = i / 3;

            SetFunctionModule(a, b, bit, isFunction);

            SetFunctionModule(b, a, bit, isFunction);
        }
    }

    private void DrawFinderPattern(int x, int y, bool[][] isFunction)
    {
        for (int dy = -4; dy <= 4; dy++)
        {
            for (int dx = -4; dx <= 4; dx++)
            {
                int dist = Math.Max(Math.Abs(dx), Math.Abs(dy));

                int xx = x + dx, yy = y + dy;

                if (0 <= xx && xx < Size && 0 <= yy && yy < Size)

                    SetFunctionModule(xx, yy, dist != 2 && dist != 4, isFunction);
            }
        }
    }

    private void DrawAlignmentPattern(int x, int y, bool[][] isFunction)
    {
        for (int dy = -2; dy <= 2; dy++)
        {
            for (int dx = -2; dx <= 2; dx++)

                SetFunctionModule(x + dx, y + dy, Math.Max(Math.Abs(dx), Math.Abs(dy)) != 1, isFunction);
        }
    }

    private void SetFunctionModule(int x, int y, bool isDark, bool[][]? isFunction)
    {
        _modules[y][x] = isDark;

        if (isFunction != null)

            isFunction[y][x] = true;
    }

    private byte[] AddEccAndInterleave(byte[] data)
    {
        int numBlocks = NUM_ERROR_CORRECTION_BLOCKS[ErrorCorrectionLevel.Ordinal][Version];

        int blockEccLen = ECC_CODEWORDS_PER_BLOCK[ErrorCorrectionLevel.Ordinal][Version];

        int rawCodewords = GetNumRawDataModules(Version) / 8;

        int numShortBlocks = numBlocks - rawCodewords % numBlocks;

        int shortBlockLen = rawCodewords / numBlocks;

        byte[][] blocks = new byte[numBlocks][];

        byte[] rsDiv = ReedSolomonComputeDivisor(blockEccLen);

        for (int i = 0, k = 0; i < numBlocks; i++)
        {
            int datLen = shortBlockLen - blockEccLen + (i < numShortBlocks ? 0 : 1);

            byte[] dat = new byte[datLen];

            Array.Copy(data, k, dat, 0, datLen);

            k += datLen;

            byte[] block = new byte[shortBlockLen + 1];

            Array.Copy(dat, 0, block, 0, dat.Length);

            byte[] ecc = ReedSolomonComputeRemainder(dat, rsDiv);

            Array.Copy(ecc, 0, block, block.Length - blockEccLen, ecc.Length);

            blocks[i] = block;
        }

        byte[] result = new byte[rawCodewords];

        for (int i = 0, k = 0; i < blocks[0].Length; i++)
        {
            for (int j = 0; j < blocks.Length; j++)
            {
                if (i != shortBlockLen - blockEccLen || j >= numShortBlocks)
                {
                    result[k] = blocks[j][i];

                    k++;
                }
            }
        }

        return result;
    }

    private void DrawCodewords(byte[] data, bool[][] isFunction)
    {
        int i = 0;

        for (int right = Size - 1; right >= 1; right -= 2)
        {
            if (right == 6)

                right = 5;

            for (int vert = 0; vert < Size; vert++)
            {
                for (int j = 0; j < 2; j++)
                {
                    int x = right - j;

                    bool upward = ((right + 1) & 2) == 0;

                    int y = upward ? Size - 1 - vert : vert;

                    if (!isFunction[y][x] && i < data.Length * 8)
                    {
                        _modules[y][x] = GetBit(data[i >>> 3], 7 - (i & 7));

                        i++;
                    }
                }
            }
        }
    }

    private void ApplyMask(int msk, bool[][] isFunction)
    {
        for (int y = 0; y < Size; y++)
        {
            for (int x = 0; x < Size; x++)
            {
                bool invert = msk switch
                {
                    0 => (x + y) % 2 == 0,
                    1 => y % 2 == 0,
                    2 => x % 3 == 0,
                    3 => (x + y) % 3 == 0,
                    4 => (x / 3 + y / 2) % 2 == 0,
                    5 => x * y % 2 + x * y % 3 == 0,
                    6 => (x * y % 2 + x * y % 3) % 2 == 0,
                    7 => ((x + y) % 2 + x * y % 3) % 2 == 0,

                    _ => false
                };

                if (!isFunction[y][x] && invert)

                    _modules[y][x] = !_modules[y][x];
            }
        }
    }

    private int GetPenaltyScore()
    {
        int result = 0;

        for (int y = 0; y < Size; y++)
        {
            bool runColor = false;

            int runX = 0;

            int[] runHistory = new int[7];

            for (int x = 0; x < Size; x++)
            {
                if (_modules[y][x] == runColor)
                {
                    runX++;

                    if (runX == 5)

                        result += PENALTY_N1;

                    else if (runX > 5)

                        result++;
                }

                else
                {
                    FinderPenaltyAddHistory(runX, runHistory);

                    if (!runColor)

                        result += FinderPenaltyCountPatterns(runHistory) * PENALTY_N3;

                    runColor = _modules[y][x];

                    runX = 1;
                }
            }

            result += FinderPenaltyTerminateAndCount(runColor, runX, runHistory) * PENALTY_N3;
        }

        for (int x = 0; x < Size; x++)
        {
            bool runColor = false;

            int runY = 0;

            int[] runHistory = new int[7];

            for (int y = 0; y < Size; y++)
            {
                if (_modules[y][x] == runColor)
                {
                    runY++;

                    if (runY == 5)

                        result += PENALTY_N1;

                    else if (runY > 5)

                        result++;
                }

                else
                {
                    FinderPenaltyAddHistory(runY, runHistory);

                    if (!runColor)

                        result += FinderPenaltyCountPatterns(runHistory) * PENALTY_N3;

                    runColor = _modules[y][x];

                    runY = 1;
                }
            }

            result += FinderPenaltyTerminateAndCount(runColor, runY, runHistory) * PENALTY_N3;
        }

        for (int y = 0; y < Size - 1; y++)
        {
            for (int x = 0; x < Size - 1; x++)
            {
                bool color = _modules[y][x];

                if (color == _modules[y][x + 1] &&

                    color == _modules[y + 1][x] &&

                    color == _modules[y + 1][x + 1])

                    result += PENALTY_N2;
            }
        }

        int dark = 0;

        for (int y = 0; y < Size; y++)
        {
            for (int x = 0; x < Size; x++)
            {
                if (_modules[y][x]) dark++;
            }
        }

        int total = Size * Size;

        int k = (Math.Abs(dark * 20 - total * 10) + total - 1) / total - 1;

        result += k * PENALTY_N4;

        return result;
    }

    private int[] GetAlignmentPatternPositions()
    {
        if (Version == 1)

            return Array.Empty<int>();

        int numAlign = Version / 7 + 2;

        int step = (Version * 8 + numAlign * 3 + 5) / (numAlign * 4 - 4) * 2;

        int[] result = new int[numAlign];

        result[0] = 6;

        for (int i = result.Length - 1, pos = Size - 7; i >= 1; i--, pos -= step)

            result[i] = pos;

        return result;
    }

    private static int GetNumRawDataModules(int ver)
    {
        int size = ver * 4 + 17;

        int result = size * size;

        result -= 8 * 8 * 3;

        result -= 15 * 2 + 1;

        result -= (size - 16) * 2;

        if (ver >= 2)
        {
            int numAlign = ver / 7 + 2;

            result -= (numAlign - 1) * (numAlign - 1) * 25;

            result -= (numAlign - 2) * 2 * 20;

            if (ver >= 7)

                result -= 6 * 3 * 2;
        }

        return result;
    }

    private static byte[] ReedSolomonComputeDivisor(int degree)
    {
        byte[] result = new byte[degree];

        result[degree - 1] = 1;

        int root = 1;

        for (int i = 0; i < degree; i++)
        {
            for (int j = 0; j < result.Length; j++)
            {
                result[j] = (byte)ReedSolomonMultiply(result[j], root);

                if (j + 1 < result.Length)

                    result[j] ^= result[j + 1];
            }

            root = ReedSolomonMultiply(root, 0x02);
        }

        return result;
    }

    private static byte[] ReedSolomonComputeRemainder(byte[] data, byte[] divisor)
    {
        byte[] result = new byte[divisor.Length];

        foreach (byte b in data)
        {
            int factor = b ^ result[0];

            Array.Copy(result, 1, result, 0, result.Length - 1);

            result[result.Length - 1] = 0;

            for (int i = 0; i < result.Length; i++)

                result[i] ^= (byte)ReedSolomonMultiply(divisor[i], factor);
        }

        return result;
    }

    private static int ReedSolomonMultiply(int x, int y)
    {
        int z = 0;

        for (int i = 7; i >= 0; i--)
        {
            z = (z << 1) ^ ((z >>> 7) * 0x11D);

            z ^= ((y >>> i) & 1) * x;
        }

        return z;
    }

    private static int GetNumDataCodewords(int ver, Ecc ecl)
    {
        return GetNumRawDataModules(ver) / 8

            - ECC_CODEWORDS_PER_BLOCK[ecl.Ordinal][ver]

            * NUM_ERROR_CORRECTION_BLOCKS[ecl.Ordinal][ver];
    }

    private int FinderPenaltyCountPatterns(int[] runHistory)
    {
        int n = runHistory[1];

        bool core = n > 0 && runHistory[2] == n && runHistory[3] == n * 3 && runHistory[4] == n && runHistory[5] == n;

        return (core && runHistory[0] >= n * 4 && runHistory[6] >= n ? 1 : 0)

             + (core && runHistory[6] >= n * 4 && runHistory[0] >= n ? 1 : 0);
    }

    private int FinderPenaltyTerminateAndCount(bool currentRunColor, int currentRunLength, int[] runHistory)
    {
        if (currentRunColor)
        {
            FinderPenaltyAddHistory(currentRunLength, runHistory);

            currentRunLength = 0;
        }

        currentRunLength += Size;

        FinderPenaltyAddHistory(currentRunLength, runHistory);

        return FinderPenaltyCountPatterns(runHistory);
    }

    private void FinderPenaltyAddHistory(int currentRunLength, int[] runHistory)
    {
        if (runHistory[0] == 0)

            currentRunLength += Size;

        Array.Copy(runHistory, 0, runHistory, 1, runHistory.Length - 1);

        runHistory[0] = currentRunLength;
    }

    private static bool GetBit(int x, int i)
    {
        return ((x >>> i) & 1) != 0;
    }

    public const int MIN_VERSION = 1;
    public const int MAX_VERSION = 40;
    private const int PENALTY_N1 = 3;
    private const int PENALTY_N2 = 3;
    private const int PENALTY_N3 = 40;
    private const int PENALTY_N4 = 10;

    private static readonly sbyte[][] ECC_CODEWORDS_PER_BLOCK = {
        new sbyte[] {-1,  7, 10, 15, 20, 26, 18, 20, 24, 30, 18, 20, 24, 26, 30, 22, 24, 28, 30, 28, 28, 28, 28, 30, 30, 26, 28, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30},
        new sbyte[] {-1, 10, 16, 26, 18, 24, 16, 18, 22, 22, 26, 30, 22, 22, 24, 24, 28, 28, 26, 26, 26, 26, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28},
        new sbyte[] {-1, 13, 22, 18, 26, 18, 24, 18, 22, 20, 24, 28, 26, 24, 20, 30, 24, 28, 28, 26, 30, 28, 30, 30, 30, 30, 28, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30},
        new sbyte[] {-1, 17, 28, 22, 16, 22, 28, 26, 26, 24, 28, 24, 28, 22, 24, 24, 30, 28, 28, 26, 28, 30, 24, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30},
    };

    private static readonly sbyte[][] NUM_ERROR_CORRECTION_BLOCKS = {
        new sbyte[] {-1, 1, 1, 1, 1, 1, 2, 2, 2, 2, 4,  4,  4,  4,  4,  6,  6,  6,  6,  7,  8,  8,  9,  9, 10, 12, 12, 12, 13, 14, 15, 16, 17, 18, 19, 19, 20, 21, 22, 24, 25},
        new sbyte[] {-1, 1, 1, 1, 2, 2, 4, 4, 4, 5, 5,  5,  8,  9,  9, 10, 10, 11, 13, 14, 16, 17, 17, 18, 20, 21, 23, 25, 26, 28, 29, 31, 33, 35, 37, 38, 40, 43, 45, 47, 49},
        new sbyte[] {-1, 1, 1, 2, 2, 4, 4, 6, 6, 8, 8,  8, 10, 12, 16, 12, 17, 16, 18, 21, 20, 23, 23, 25, 27, 29, 34, 34, 35, 38, 40, 43, 45, 48, 51, 53, 56, 59, 62, 65, 68},
        new sbyte[] {-1, 1, 1, 2, 4, 4, 4, 5, 6, 8, 8, 11, 11, 16, 16, 18, 16, 19, 21, 25, 25, 25, 34, 30, 32, 35, 37, 40, 42, 45, 48, 51, 54, 57, 60, 63, 66, 70, 74, 77, 81},
    };
}
