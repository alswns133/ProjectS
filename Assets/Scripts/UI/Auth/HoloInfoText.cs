using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace ProjectS.UI
{
    /// <summary>
    /// 홀로그램 정보창에 늘어놓을 "의미 있어 보이는" 무작위 데이터를 만든다. 로그인 화면 장식 전용.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 순수한 0/1 나열은 금방 벽지처럼 읽혀서, 실제 시스템 로그처럼 보이도록 <b>줄마다 형식을 섞는다</b>:
    /// 이진수 묶음 · 메모리 주소 + 이진수 · 태그 + 이진수 + 상태어 · 진행 막대. 머리줄에는 노드 번호와 작업명을 둔다.
    /// 읽을 수 있는 단어(ACK, SYNC, TRACE…)가 드문드문 섞여 있어야 "무언가를 처리 중"인 화면으로 읽힌다.
    /// </para>
    /// <para>
    /// 색은 단일 파랑 원칙대로 파랑 명도만 3단계로 나눈다(상태어 밝게 · 이진수 중간 · 주소 어둡게).
    /// 서식 태그 안에는 0/1 숫자가 들어가지 않게 골랐다 — 깜빡임(비트 뒤집기)이 태그를 깨뜨리지 않도록.
    /// </para>
    /// </remarks>
    public static class HoloInfoText
    {
        // 서식 태그에 0/1이 없어야 한다(ScrambleIndices 밖의 글자라도 실수로 뒤집히면 태그가 깨짐).
        private const string Bright = "<color=#CFE6FF>";
        private const string Mid = "<color=#7FB2EE>";
        private const string Dim = "<color=#4C78B8>";
        private const string EndColor = "</color>";

        private static readonly string[] Tasks =
        {
            "TRACE", "UPLINK", "DECRYPT", "SIGNAL", "ARCHIVE", "HANDSHAKE",
            "MEMDUMP", "NEURAL LINK", "INTEGRITY", "SECTOR SCAN", "CIPHER", "RELAY",
        };

        private static readonly string[] Tags = { "SIG", "AUTH", "NODE", "MEM", "NET", "KEY", "SYS", "IO" };
        private static readonly string[] States = { "ACK", "SYNC", "HOLD", "PASS", "NULL", "XFER", "LOCK", "DROP" };
        private static readonly string[] Statuses = { "SYNC", "LIVE", "LOCK", "WAIT", "LINK" };

        /// <summary>
        /// 머리줄(왼쪽 제목, 오른쪽 상태)을 만든다.
        /// </summary>
        public static void BuildHeader(out string title, out string status)
        {
            title = $"NODE {Hex(2)}-{Hex(2)} // {Pick(Tasks)}";
            status = Random.value < 0.5f
                ? $"{Random.Range(12, 100)}%"
                : Pick(Statuses);
        }

        /// <summary>
        /// 본문을 만든다. 깜빡임 연출이 뒤집을 수 있는 글자(이진수 자리)의 위치를 함께 돌려준다.
        /// </summary>
        /// <param name="columns">한 줄 최대 글자 수(보이는 글자 기준)</param>
        /// <param name="lines">줄 수</param>
        /// <param name="scrambleIndices">뒤집어도 되는 0/1 글자의 문자열 내 위치(서식 태그 포함 기준)</param>
        public static string BuildBody(int columns, int lines, List<int> scrambleIndices)
        {
            scrambleIndices.Clear();
            var sb = new StringBuilder(columns * lines * 3);

            for (int i = 0; i < lines; i++)
            {
                if (i > 0) sb.Append('\n');

                float roll = Random.value;
                if (roll < 0.52f) AppendBinaryLine(sb, columns, scrambleIndices);
                else if (roll < 0.74f) AppendAddressLine(sb, columns, scrambleIndices);
                else if (roll < 0.90f) AppendTagLine(sb, columns, scrambleIndices);
                else AppendProgressLine(sb, columns);
            }

            return sb.ToString();
        }

        // 0110 1001 1101 … (4비트 묶음, 가끔 8비트)
        private static void AppendBinaryLine(StringBuilder sb, int columns, List<int> scramble)
        {
            sb.Append(Mid);
            AppendBits(sb, columns, scramble);
            sb.Append(EndColor);
        }

        // 0x3F2A  0110 1001 …
        private static void AppendAddressLine(StringBuilder sb, int columns, List<int> scramble)
        {
            string address = "0x" + Hex(4);
            sb.Append(Dim).Append(address).Append(EndColor).Append("  ");
            sb.Append(Mid);
            AppendBits(sb, columns - address.Length - 2, scramble);
            sb.Append(EndColor);
        }

        // [SIG] 01101 > ACK
        private static void AppendTagLine(StringBuilder sb, int columns, List<int> scramble)
        {
            string tag = $"[{Pick(Tags)}] ";
            string state = " > " + Pick(States);
            int bits = Mathf.Clamp(columns - tag.Length - state.Length, 4, 12);

            sb.Append(Bright).Append(tag).Append(EndColor);
            sb.Append(Mid);
            for (int b = 0; b < bits; b++)
            {
                scramble.Add(sb.Length);
                sb.Append(Random.value < 0.5f ? '0' : '1');
            }
            sb.Append(EndColor);
            sb.Append(Bright).Append(state).Append(EndColor);
        }

        // [||||||....] 61%
        private static void AppendProgressLine(StringBuilder sb, int columns)
        {
            int bar = Mathf.Clamp(columns - 7, 4, 20);
            float ratio = Random.value;
            int filled = Mathf.RoundToInt(bar * ratio);

            sb.Append(Dim).Append('[').Append(EndColor);
            sb.Append(Bright).Append('|', filled).Append(EndColor);
            sb.Append(Dim).Append('.', bar - filled).Append("] ").Append(EndColor);
            sb.Append(Bright).Append(Mathf.RoundToInt(ratio * 100f)).Append('%').Append(EndColor);
        }

        private static void AppendBits(StringBuilder sb, int columns, List<int> scramble)
        {
            int written = 0;
            while (written < columns)
            {
                int group = Random.value < 0.8f ? 4 : 8;
                if (written + group > columns) break;
                if (written > 0)
                {
                    sb.Append(' ');
                    written++;
                    if (written + group > columns) break;
                }

                for (int b = 0; b < group; b++)
                {
                    scramble.Add(sb.Length);
                    sb.Append(Random.value < 0.5f ? '0' : '1');
                }
                written += group;
            }
        }

        private static string Hex(int digits)
        {
            const string chars = "0123456789ABCDEF";
            var sb = new StringBuilder(digits);
            for (int i = 0; i < digits; i++) sb.Append(chars[Random.Range(0, 16)]);
            return sb.ToString();
        }

        private static string Pick(string[] items) => items[Random.Range(0, items.Length)];
    }
}
