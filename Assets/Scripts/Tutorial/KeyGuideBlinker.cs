using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace ProjectS.Tutorials
{
    /// <summary>
    /// 튜토리얼 조작 안내의 키보드 이미지를 어두운 색으로 점멸시켜 "이 키를 눌러라"를 유도한다.
    /// 어떤 키를, 동시에 또는 순서대로 점멸할지를 인스펙터에서 정한다.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 붙이는 곳: 안내 묶음 오브젝트(TutorialMoveGuide 등) 하나에 하나씩. <see cref="playOnEnable"/>이 켜져 있으면
    /// 오브젝트가 켜질 때 점멸을 시작하고 꺼질 때 멈춘다 — 안내를 켜고 끄는 트리거(ZoneTrigger 등)와
    /// 따로 연결할 필요가 없게 하려는 것이다.
    /// </para>
    /// <para>
    /// <b>색은 원래 색과 <see cref="dimColor"/> 사이를 오간다.</b> 원래 색은 처음 켜질 때 한 번 기억하고, 멈추면 그 색으로
    /// 되돌린다. 되돌리지 않으면 어두운 순간에 꺼진 키가 다음에 켜졌을 때 회색으로 굳어 있다.
    /// Image의 색은 스프라이트에 곱해지므로 흰 키보드 스프라이트는 dimColor 그대로, 테두리·글자 선은 더 어둡게 보인다.
    /// </para>
    /// <para>
    /// <b>시간은 unscaled로 센다.</b> 안내가 떠 있는 동안 연출로 timeScale을 늦춰도 점멸은 같은 속도여야 한다.
    /// (2026-09-17 TH)
    /// </para>
    /// </remarks>
    public class KeyGuideBlinker : MonoBehaviour
    {
        /// <summary>여러 단계를 어떻게 재생할지.</summary>
        public enum BlinkOrder
        {
            /// <summary>모든 단계의 키가 한꺼번에 점멸한다.</summary>
            Simultaneous,

            /// <summary>단계 0부터 차례로 점멸한다. 차례가 아닌 키는 원래 색으로 있다.</summary>
            Sequential,
        }

        /// <summary>한 번에 함께 점멸하는 키 묶음. 순서 모드에서는 이 묶음이 한 차례다.</summary>
        [Serializable]
        public class BlinkStep
        {
            [Tooltip("이 차례에 함께 점멸할 키 이미지. 한 차례에 여러 키를 넣어도 된다(예: A·D 동시).")]
            public Graphic[] keys;
        }

        [Header("대상")]
        [Tooltip("점멸할 키 묶음. 동시 모드에서는 전부 함께, 순서 모드에서는 위에서부터 차례로 점멸한다.")]
        [SerializeField] private List<BlinkStep> steps = new List<BlinkStep>();

        [SerializeField] private BlinkOrder order = BlinkOrder.Simultaneous;

        [Header("색")]
        [Tooltip("가장 어두워졌을 때의 색. 원래 색(흰색)과 이 색 사이를 오간다.")]
        [SerializeField] private Color dimColor = new Color(0.35f, 0.35f, 0.38f, 1f);

        [Header("타이밍")]
        [Tooltip("어두워졌다 원래 색으로 돌아오는 한 번의 점멸 시간(초).")]
        [SerializeField, Min(0.05f)] private float blinkPeriod = 0.8f;

        [Tooltip("켜면 부드럽게 어두워졌다 밝아지고, 끄면 딱딱 끊어서 바뀐다.")]
        [SerializeField] private bool smooth = true;

        [Tooltip("순서 모드: 한 차례에 몇 번 점멸하고 다음 키로 넘어갈지. " +
                 "반복을 끈 동시 모드: 전체가 몇 번 점멸하고 멈출지.")]
        [SerializeField, Min(1)] private int blinksPerStep = 2;

        [Tooltip("시작 전 대기(초). 독백이 먼저 뜨고 조금 뒤에 키를 짚어 주고 싶을 때 쓴다.")]
        [SerializeField, Min(0f)] private float startDelay = 0f;

        [Tooltip("한 바퀴(순서 모드의 마지막 차례, 동시 모드의 blinksPerStep번)를 마친 뒤 다음 바퀴 전까지 쉬는 시간(초).")]
        [SerializeField, Min(0f)] private float loopInterval = 0f;

        [Tooltip("끄면 한 바퀴만 돌고 원래 색으로 멈춘다.")]
        [SerializeField] private bool loop = true;

        [Tooltip("오브젝트가 켜질 때 자동으로 시작하고 꺼질 때 멈춘다.")]
        [SerializeField] private bool playOnEnable = true;

        /// <summary>지금 점멸 중인지.</summary>
        public bool IsPlaying { get; private set; }

        // 키마다 처음 색. 멈출 때 되돌리고, 점멸의 밝은 쪽 끝으로 쓴다.
        private readonly Dictionary<Graphic, Color> originalColors = new Dictionary<Graphic, Color>();

        // 이번 프레임에 키별로 적용할 어두움(0~1). 매 프레임 비우고 다시 채운다(할당 없이 재사용).
        private readonly Dictionary<Graphic, float> frameDims = new Dictionary<Graphic, float>();

        private float elapsed;

        private void OnEnable()
        {
            if (playOnEnable) Play();
        }

        private void OnDisable()
        {
            Stop();
        }

        /// <summary>처음부터 점멸을 시작한다. 이미 점멸 중이면 처음으로 되돌린다.</summary>
        public void Play()
        {
            CacheOriginalColors();
            RestoreAll();

            elapsed = 0f;
            IsPlaying = true;
        }

        /// <summary>점멸을 멈추고 모든 키를 원래 색으로 되돌린다.</summary>
        public void Stop()
        {
            IsPlaying = false;
            RestoreAll();
        }

        private void Update()
        {
            if (!IsPlaying) return;

            elapsed += Time.unscaledDeltaTime;

            float t = elapsed - startDelay;
            if (t < 0f) return;

            if (order == BlinkOrder.Simultaneous) UpdateSimultaneous(t);
            else UpdateSequential(t);
        }

        private void UpdateSimultaneous(float t)
        {
            float roundLength = blinkPeriod * blinksPerStep;

            if (!loop && t >= roundLength)
            {
                Stop();
                return;
            }

            float roundTime = loop ? t % (roundLength + loopInterval) : t;

            // 한 바퀴 뒤 쉬는 구간이면 원래 색으로 둔다.
            float dim = roundTime < roundLength ? DimAmount(roundTime) : 0f;

            BeginFrame();
            for (int i = 0; i < steps.Count; i++)
            {
                AccumulateStep(steps[i], dim);
            }
            ApplyFrame();
        }

        private void UpdateSequential(float t)
        {
            if (steps.Count == 0) return;

            float stepLength = blinkPeriod * blinksPerStep;
            float roundLength = stepLength * steps.Count;

            if (!loop && t >= roundLength)
            {
                Stop();
                return;
            }

            float roundTime = loop ? t % (roundLength + loopInterval) : t;
            int current = Mathf.FloorToInt(roundTime / stepLength);   // 쉬는 구간이면 steps.Count 이상이 나온다

            BeginFrame();
            for (int i = 0; i < steps.Count; i++)
            {
                float dim = i == current ? DimAmount(roundTime - stepLength * i) : 0f;
                AccumulateStep(steps[i], dim);
            }
            ApplyFrame();
        }

        // 한 번의 점멸 안에서 얼마나 어두운지(0=원래 색, 1=dimColor). 0에서 시작해야 차례가 바뀌는 순간 색이 튀지 않는다.
        private float DimAmount(float time)
        {
            float phase = (time % blinkPeriod) / blinkPeriod;

            if (smooth) return 0.5f - 0.5f * Mathf.Cos(phase * Mathf.PI * 2f);

            return phase < 0.5f ? 1f : 0f;
        }

        // ── 프레임 합성 ──────────────────────────────────────────
        // 한 키가 여러 차례에 들어갈 수 있다(예: Shift+W, Shift+A… 에서 Shift). 차례마다 바로 색을 칠하면
        // 마지막 차례가 "내 차례 아님 = 원래 색"으로 덮어써, 공유 키가 마지막 차례에서만 점멸한다.
        // 그래서 키별로 이번 프레임의 가장 어두운 값을 모은 뒤 한 번만 칠한다.

        private void BeginFrame()
        {
            frameDims.Clear();
        }

        private void AccumulateStep(BlinkStep step, float dim)
        {
            if (step?.keys == null) return;

            foreach (Graphic key in step.keys)
            {
                if (key == null) continue;

                frameDims[key] = frameDims.TryGetValue(key, out float current) ? Mathf.Max(current, dim) : dim;
            }
        }

        private void ApplyFrame()
        {
            foreach (KeyValuePair<Graphic, float> pair in frameDims)
            {
                if (!originalColors.TryGetValue(pair.Key, out Color original)) continue;

                pair.Key.color = Color.Lerp(original, dimColor, pair.Value);
            }
        }

        // 처음 본 키의 색만 기억한다. 점멸 도중 색을 다시 읽으면 어두운 색을 원래 색으로 착각해 점점 어두워진다.
        private void CacheOriginalColors()
        {
            foreach (BlinkStep step in steps)
            {
                if (step?.keys == null) continue;

                foreach (Graphic key in step.keys)
                {
                    if (key != null && !originalColors.ContainsKey(key)) originalColors.Add(key, key.color);
                }
            }
        }

        private void RestoreAll()
        {
            foreach (KeyValuePair<Graphic, Color> pair in originalColors)
            {
                if (pair.Key != null) pair.Key.color = pair.Value;
            }
        }
    }
}
