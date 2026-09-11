using System;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;

namespace ProjectS.Logging
{
    /// <summary>
    /// 빌드에서 F8로 여닫는 IMGUI 기반 로그 오버레이다.
    /// 문자열은 로그·필터 변경 때만 다시 만들고 OnGUI에서는 캐시를 그대로 그린다.
    /// </summary>
    internal sealed class LogOverlayConsole : MonoBehaviour, ILogSink
    {
        private readonly StringBuilder displayBuilder = new(4096);

        private LogEntry[] entries;
        private int nextIndex;
        private int count;
        private bool visible;
        private bool displayDirty;
        private int severityFilter = -1;
        private int sourceFilter = -1;
        private int categoryFilter = -1;
        private string cachedDisplay = string.Empty;
        private Vector2 scroll;
        private GUIStyle logStyle;

        public void Configure(LogSettings settings)
        {
            entries = new LogEntry[settings.OverlayCapacity];
            visible = settings.OverlayStartsVisible;
            displayDirty = true;
        }

        public void Write(in LogEntry entry)
        {
            if (entries == null || entries.Length == 0) return;

            entries[nextIndex] = entry;
            nextIndex = (nextIndex + 1) % entries.Length;
            if (count < entries.Length) count++;
            displayDirty = true;
        }

        private void Update()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null && keyboard.f8Key.wasPressedThisFrame)
                visible = !visible;
        }

        private void OnGUI()
        {
            if (!visible) return;

            if (logStyle == null)
            {
                logStyle = new GUIStyle(GUI.skin.label)
                {
                    fontSize = 15,
                    richText = true,
                    wordWrap = true,
                    alignment = TextAnchor.UpperLeft,
                };
            }

            if (displayDirty) RebuildDisplay();

            Rect area = new Rect(16f, 72f, Screen.width - 32f, Screen.height - 88f);
            GUI.color = new Color(0f, 0f, 0f, 0.82f);
            GUI.DrawTexture(area, Texture2D.whiteTexture);
            GUI.color = Color.white;

            GUILayout.BeginArea(area);
            GUILayout.BeginHorizontal();
            GUILayout.Label($"Log Console ({count}/{entries.Length}) · F8", GUILayout.Width(190f));

            if (GUILayout.Button($"Severity: {FilterName<LogSeverity>(severityFilter)}", GUILayout.Width(165f)))
                CycleSeverityFilter();
            if (GUILayout.Button($"Source: {FilterName<LogSource>(sourceFilter)}", GUILayout.Width(145f)))
                CycleSourceFilter();
            if (GUILayout.Button($"Category: {FilterName<LogCategory>(categoryFilter)}", GUILayout.Width(180f)))
                CycleCategoryFilter();
            if (GUILayout.Button("Clear", GUILayout.Width(70f))) Clear();
            if (GUILayout.Button("Close", GUILayout.Width(70f))) visible = false;
            GUILayout.EndHorizontal();

            scroll = GUILayout.BeginScrollView(scroll);
            GUILayout.Label(cachedDisplay, logStyle);
            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        private void Clear()
        {
            nextIndex = 0;
            count = 0;
            displayDirty = true;
        }

        private void RebuildDisplay()
        {
            displayDirty = false;
            displayBuilder.Clear();

            for (int i = 0; i < count; i++)
            {
                LogEntry entry = entries[(nextIndex - count + i + entries.Length) % entries.Length];
                if (!PassesFilter(entry)) continue;

                DateTimeOffset timestamp = DateTimeOffset.FromUnixTimeMilliseconds(entry.TimestampUtcMs);
                displayBuilder.Append("<color=#AAAAAA>");
                displayBuilder.Append(timestamp.ToString("HH:mm:ss.fff'Z'"));
                displayBuilder.Append("</color> ");
                AppendColor(entry.Severity.ToString(), SeverityColor(entry.Severity));
                displayBuilder.Append(' ');
                AppendColor(entry.Source.ToString(), SourceColor(entry.Source));
                displayBuilder.Append('/');
                AppendColor(entry.Category.ToString(), CategoryColor(entry.Category));
                displayBuilder.Append(' ');
                displayBuilder.Append(EscapeRichText(entry.Message));

                if (!string.IsNullOrEmpty(entry.StackTrace))
                {
                    displayBuilder.Append('\n');
                    displayBuilder.Append("<color=#B0B0B0>");
                    displayBuilder.Append(EscapeRichText(entry.StackTrace));
                    displayBuilder.Append("</color>");
                }
                displayBuilder.Append('\n');
            }

            cachedDisplay = displayBuilder.ToString();
            scroll.y = float.MaxValue;
        }

        private bool PassesFilter(LogEntry entry)
        {
            return (severityFilter < 0 || (int)entry.Severity == severityFilter) &&
                   (sourceFilter < 0 || (int)entry.Source == sourceFilter) &&
                   (categoryFilter < 0 || (int)entry.Category == categoryFilter);
        }

        private void CycleSeverityFilter()
        {
            severityFilter = NextFilter<LogSeverity>(severityFilter);
            displayDirty = true;
        }

        private void CycleSourceFilter()
        {
            sourceFilter = NextFilter<LogSource>(sourceFilter);
            displayDirty = true;
        }

        private void CycleCategoryFilter()
        {
            categoryFilter = NextFilter<LogCategory>(categoryFilter);
            displayDirty = true;
        }

        private static int NextFilter<TEnum>(int current) where TEnum : Enum
        {
            int next = current + 1;
            return next >= Enum.GetValues(typeof(TEnum)).Length ? -1 : next;
        }

        private static string FilterName<TEnum>(int filter) where TEnum : Enum =>
            filter < 0 ? "All" : ((TEnum)(object)filter).ToString();

        private void AppendColor(string value, string hexColor)
        {
            displayBuilder.Append("<color=");
            displayBuilder.Append(hexColor);
            displayBuilder.Append('>');
            displayBuilder.Append(value);
            displayBuilder.Append("</color>");
        }

        private static string EscapeRichText(string value) =>
            string.IsNullOrEmpty(value) ? string.Empty : value.Replace("<", "&lt;").Replace(">", "&gt;");

        private static string SeverityColor(LogSeverity severity) => severity switch
        {
            LogSeverity.Info => "#D9D9D9",
            LogSeverity.Warning => "#FFD166",
            LogSeverity.Error => "#FF7B7B",
            _ => "#FF3B3B",
        };

        private static string SourceColor(LogSource source) => source switch
        {
            LogSource.Client => "#7DD3FC",
            LogSource.Server => "#C4B5FD",
            _ => "#94A3B8",
        };

        private static string CategoryColor(LogCategory category) => category switch
        {
            LogCategory.Network => "#5EEAD4",
            LogCategory.Combat => "#FDA4AF",
            LogCategory.UI => "#FDE68A",
            LogCategory.Data => "#A7F3D0",
            LogCategory.Sound => "#C4B5FD",
            LogCategory.Scene => "#93C5FD",
            _ => "#CBD5E1",
        };
    }
}
