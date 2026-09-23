using System;
using UnityEngine;
using ProjectS.Effects;
using ProjectS.Events;
using ProjectS.Settings;

namespace ProjectS.UI
{
    /// <summary>
    /// CombatEvents의 데미지 발생 이벤트를 받아 월드 데미지 텍스트를 띄운다.
    /// 전투 로직은 숫자·위치·종류만 발행하고, 색과 문구 같은 연출 결정은 여기서만 한다.
    /// </summary>
    public class DamageTextSpawner : PooledSpawner<DamageText>
    {
        /// <summary>
        /// 데미지 텍스트 종류 하나의 표시 방식. 색과 문구를 한 묶음으로 두어
        /// "치명타는 노란색에 CRITICAL 문구" 같은 규칙이 인스펙터 한 곳에서 읽히게 한다.
        /// </summary>
        [Serializable]
        private class TextStyle
        {
            public Color color = Color.white;

            // {0}에 데미지 숫자가 들어간다. TMP 리치 텍스트 태그를 그대로 쓸 수 있어
            // "<size=50%>CRITICAL</size>\n{0}"처럼 라벨만 작게 줄이는 조절이 가능하다.
            // (태그 규칙: 여는 쪽에만 값을 쓰고 "<size=50%>", 닫는 쪽은 값 없이 "</size>")
            // 여러 줄 입력이 필요해 TextArea로 둔다(단일 줄 필드로는 개행을 넣을 수 없다).
            [TextArea(1, 3)]
            public string format = "{0}";
        }

        // 같은 위치에 여러 타격이 들어와도 숫자가 완전히 겹치지 않게 살짝 흩뿌린다.
        [SerializeField] private float spawnJitter = 0.3f;

        [Header("표시 종류별 스타일")]
        [SerializeField]
        private TextStyle normal = new TextStyle { color = Color.white, format = "{0}" };

        // CRITICAL 라벨은 숫자보다 작게 띄운다 — 읽어야 할 정보는 숫자이고 라벨은 보조라서.
        // 크기·문구·줄바꿈은 전부 이 문자열에서 조절한다.
        [SerializeField]
        private TextStyle critical = new TextStyle
        {
            color = Color.yellow,
            format = "<size=50%>CRITICAL</size>\n{0}",
        };

        [SerializeField]
        private TextStyle playerDamaged = new TextStyle { color = Color.red, format = "{0}" };

        private void OnEnable() => CombatEvents.OnDamageDealt += OnDamageDealt;
        private void OnDisable() => CombatEvents.OnDamageDealt -= OnDamageDealt;

        private void OnDamageDealt(Vector3 worldPos, int amount, DamageTextKind kind)
        {
            // 옵션 [데미지 숫자 표시]. 발행하는 전투 쪽이 아니라 여기서 막아야 몬스터·플레이어 피격이
            // 한 곳에서 같이 꺼진다. 이미 떠 있는 숫자는 수명대로 사라지게 둔다.
            if (!GameSettings.Current.ShowDamageNumbers) return;

            Vector2 jitter = UnityEngine.Random.insideUnitCircle * spawnJitter;
            Vector3 position = worldPos + new Vector3(jitter.x, 0f, jitter.y);

            TextStyle style = GetStyle(kind);
            GetFromPool().Show(string.Format(style.format, amount), position, style.color, ReturnToPool);
        }

        // 알 수 없는 종류가 들어와도 연출이 멈추지 않게 일반 스타일로 떨어뜨린다.
        private TextStyle GetStyle(DamageTextKind kind) => kind switch
        {
            DamageTextKind.Critical => critical,
            DamageTextKind.PlayerDamaged => playerDamaged,
            _ => normal,
        };
    }
}
