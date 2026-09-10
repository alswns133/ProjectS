using System;
using System.Collections.Generic;
using UnityEngine;

namespace ProjectS.Debugging
{
    /// <summary>
    /// 화면에 로그를 직접 그리는 개발용 오버레이. 빌드(로그 파일을 못 남기는 상황)에서 <c>[진단]</c> 로그를
    /// 눈으로 확인하려고 둔다. <b>스크롤 가능</b> + 각 줄에 <b>벽시계 타임스탬프</b>(HH:mm:ss.fff)를 붙여,
    /// 같은 PC의 두 빌드가 같은 시계를 봐 타이밍을 맞대 볼 수 있다.
    /// </summary>
    /// <remarks>
    /// 빈 GameObject에 붙여 부트스트랩 씬에 둔다(DontDestroyOnLoad). 확인 끝나면 오브젝트째 지운다.
    /// </remarks>
    public class OnScreenLog : MonoBehaviour
    {
        [Tooltip("버퍼에 유지할 최대 줄 수(스크롤 가능하니 넉넉히).")]
        [SerializeField] private int maxLines = 300;

        [Tooltip("이 문자열이 든 로그만 표시. 비우면 전부 표시.")]
        [SerializeField] private string filter = "[진단]";

        [Tooltip("글자 크기.")]
        [SerializeField] private int fontSize = 20;

        [Tooltip("켜면 새 로그가 올 때 자동으로 맨 아래로(라이브). 끄면 스크롤 위치 유지(타이밍 검토용).")]
        [SerializeField] private bool autoScroll = true;

        private readonly List<string> lines = new();
        private Vector2 scroll;
        private GUIStyle style;
        private bool dirty;

        private void Awake() => DontDestroyOnLoad(gameObject);

        private void OnEnable() => Application.logMessageReceived += OnLog;
        private void OnDisable() => Application.logMessageReceived -= OnLog;

        private void OnLog(string message, string stackTrace, LogType type)
        {
            if (!string.IsNullOrEmpty(filter) && !message.Contains(filter)) return;

            // 벽시계 타임스탬프 — 같은 PC의 두 빌드가 같은 시계라 A/B 이벤트 순서를 맞댈 수 있다.
            lines.Add($"[{DateTime.Now:HH:mm:ss.fff}] {message}");
            while (lines.Count > maxLines) lines.RemoveAt(0);
            dirty = true;
        }

        private void OnGUI()
        {
            if (style == null)
                style = new GUIStyle(GUI.skin.label) { fontSize = fontSize, richText = false, wordWrap = true };

            Rect area = new Rect(10, 10, Screen.width - 20, Screen.height - 20);

            // 검은 반투명 배경(어느 씬에서든 읽히게).
            GUI.color = new Color(0f, 0f, 0f, 0.65f);
            GUI.DrawTexture(area, Texture2D.whiteTexture);
            GUI.color = Color.white;

            GUILayout.BeginArea(area);

            // 상단 툴바: Clear / 자동스크롤 토글.
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Clear", GUILayout.Width(80), GUILayout.Height(30))) lines.Clear();
            autoScroll = GUILayout.Toggle(autoScroll, " AutoScroll", GUILayout.Height(30));
            GUILayout.Label($"  {lines.Count} lines", GUILayout.Height(30));
            GUILayout.EndHorizontal();

            // 새 로그가 왔고 자동스크롤이면 맨 아래로.
            if (autoScroll && dirty) { scroll.y = float.MaxValue; dirty = false; }

            scroll = GUILayout.BeginScrollView(scroll);
            for (int i = 0; i < lines.Count; i++)
                GUILayout.Label(lines[i], style);
            GUILayout.EndScrollView();

            GUILayout.EndArea();
        }
    }
}
