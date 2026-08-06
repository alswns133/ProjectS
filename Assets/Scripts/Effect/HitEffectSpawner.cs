using System;
using System.Collections.Generic;
using UnityEngine;
using ProjectS.Events;
using ProjectS.Players;

namespace ProjectS.Effects
{
    /// <summary>
    /// CombatEvents의 타격 접점 이벤트를 받아 맞은 부위에 히트 이펙트를 재생한다.
    /// source로 어느 쪽 접점을 들을지 고른다: 플레이어의 공격 적중(적 몸에 붙는 타격 이펙트)과
    /// 몬스터의 공격 적중(플레이어 몸에 붙는 피격 이펙트)은 연출이 달라야 하므로,
    /// 씬에 이 스포너를 방향별로 하나씩 두고 각각 다른 프리팹을 할당한다.
    /// OnDamageDealt가 아닌 접점 이벤트를 구독하는 이유: 전자의 좌표는 데미지 텍스트용
    /// 머리 위 고정 높이라, 맞은 부위에 붙어야 하는 피격 이펙트에는 접점 좌표가 필요하다.
    ///
    /// ★ 공격마다 다른 이펙트(2026-08, 민준 제안): PooledSpawner(프리팹 하나 고정)에서
    ///   KeyedPooledSpawner(프리팹별로 풀을 나눠 관리하는 기존 베이스, ProjectileSpawner와 동일)로
    ///   교체했다. 이벤트가 실어 보내는 키(HitBoxSlot/ProjectileSlot의 키, 예: "Attack1", "Skill4_1")로
    ///   슬롯을 찾아 그 이펙트를 재생하고, 키가 없거나 못 찾으면 Default Prefab으로 대체한다.
    ///   enum이 아니라 문자열 키를 쓰는 이유: 이 프로젝트의 다른 모든 슬롯(PlayerEffects,
    ///   PlayerCameraEffects, HitBoxSlot, ProjectileSlot)이 전부 문자열 키 + AnimationEventKey.Normalize
    ///   방식이다. 여기만 enum을 쓰면 새 공격이 늘 때마다 재컴파일이 필요해 나머지와 어긋난다.
    ///   ★ 마이그레이션 주의: 베이스 클래스가 바뀌면서 기존에 연결해 둔 단일 프리팹 참조는
    ///   보존되지 않는다. 씬/프리팹을 열어 Default Prefab에 그 프리팹을 다시 연결해야 한다.
    /// </summary>
    public class HitEffectSpawner : KeyedPooledSpawner<HitEffect>
    {
        // 어느 방향의 타격 접점을 구독할지. 기존 씬의 스포너는 기본값(PlayerAttack)이라
        // 분리 이전과 동일하게 "플레이어가 적을 때린" 이펙트를 재생한다.
        // 투사체가 벽에 박히는 이펙트는 여기에 넣지 않는다 — 이펙트가 총알 종류마다 다르므로
        // 씬 스포너가 프리팹을 소유하는 이 구조로는 표현할 수 없다. ProjectileImpactSpawner가 담당한다.
        private enum HitSource
        {
            PlayerAttack,   // 플레이어의 공격이 적에게 적중 → 적 몸의 타격 이펙트
            EnemyAttack,    // 몬스터의 공격이 플레이어에게 적중 → 플레이어 몸의 피격 이펙트
        }

        [SerializeField] private HitSource source = HitSource.PlayerAttack;

        // 키가 비었거나 목록에 없을 때 재생할 이펙트. 비워 두면 그 타격은 연출 없이 지나간다
        // (기존 동작을 유지하려면 예전에 쓰던 단일 프리팹을 여기로 옮겨 연결할 것).
        [SerializeField] private HitEffect defaultPrefab;

        // key -> 프리팹. 풀 사이즈(prewarm)는 베이스(KeyedPooledSpawner)의 Prewarm 배열에서 별도로 잡는다
        // — "무엇을 키로 찾을지"와 "얼마나 미리 만들어 둘지"는 다른 관심사라 배열을 나눴다.
        [Serializable]
        private class EffectEntry
        {
            public string key;
            public HitEffect prefab;
        }

        [SerializeField] private EffectEntry[] effects;

        // 매 이벤트마다 배열을 뒤지지 않도록 Awake에서 1회 구축하는 조회용 사전.
        private readonly Dictionary<string, HitEffect> effectMap = new Dictionary<string, HitEffect>();

        protected override void Awake()
        {
            base.Awake();

            if (effects == null) return;

            foreach (EffectEntry entry in effects)
            {
                if (entry == null || string.IsNullOrEmpty(entry.key) || entry.prefab == null) continue;

                // 다른 슬롯 시스템과 같은 방침: 언더바 표기 차이를 흡수하려 정규화한 키로 등록·조회한다
                // (AnimationEventKey 참조). 클립이 "Skill_4_1", 인스펙터가 "Skill41"이어도 맞물린다.
                string normKey = AnimationEventKey.Normalize(entry.key);

                if (effectMap.ContainsKey(normKey))
                {
                    Debug.LogWarning($"Duplicate hit effect key '{entry.key}'. Only the first entry is used.", this);
                    continue;
                }

                effectMap.Add(normKey, entry.prefab);
            }
        }

        private void OnEnable()
        {
            if (source == HitSource.PlayerAttack) CombatEvents.OnPlayerHitLanded += OnHitLanded;
            else CombatEvents.OnEnemyHitLanded += OnHitLanded;
        }

        private void OnDisable()
        {
            if (source == HitSource.PlayerAttack) CombatEvents.OnPlayerHitLanded -= OnHitLanded;
            else CombatEvents.OnEnemyHitLanded -= OnHitLanded;
        }

        private void OnHitLanded(Vector3 hitPos, string key)
        {
            HitEffect prefab = ResolvePrefab(key);
            if (prefab == null) return;

            GetFromPool(prefab).Play(hitPos, GetReturnCallback(prefab));
        }

        // 키로 등록된 이펙트를 찾고, 없으면 기본 이펙트로 대체한다.
        // 키 오타를 경고로 남기지 않는 이유: 다른 슬롯 시스템과 달리 여기는 매 히트마다 지나가는
        // 흔한 경로라, 매번 경고를 쌓으면 콘솔이 도배된다. 기본값으로 조용히 대체해도
        // "이펙트가 아예 안 나옴" 수준으로 눈에 띄어 튜닝 중 놓치기 어렵다.
        private HitEffect ResolvePrefab(string key)
        {
            if (!string.IsNullOrEmpty(key) && effectMap.TryGetValue(AnimationEventKey.Normalize(key), out HitEffect found))
                return found;

            return defaultPrefab;
        }
    }
}
