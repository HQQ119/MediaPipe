using System;
using System.Collections.Generic;
using System.Text;

namespace SignCoach
{
    // 按字节累积到换行再解 UTF-8，兼容拆包、粘包和多字节字符拆分。
    public sealed class JsonLineBuffer
    {
        const int MaximumBytes = 65536;
        readonly List<byte> bytes = new List<byte>();
        readonly Encoding utf8 = new UTF8Encoding(false, true);
        bool dropping;
        public int RejectedLines { get; private set; }

        public void Feed(byte[] input, int count, Action<string> receive)
        {
            for (int i = 0; i < count; i++)
            {
                byte value = input[i];
                if (value == 10)
                {
                    if (!dropping && bytes.Count > 0)
                    {
                        string line = null;
                        try { line = utf8.GetString(bytes.ToArray()).TrimEnd('\r'); }
                        catch (DecoderFallbackException) { RejectedLines++; }
                        if (!string.IsNullOrWhiteSpace(line)) receive(line);
                    }
                    bytes.Clear(); dropping = false;
                }
                else if (!dropping)
                {
                    if (bytes.Count >= MaximumBytes) { bytes.Clear(); dropping = true; RejectedLines++; }
                    else bytes.Add(value);
                }
            }
        }
    }
}
