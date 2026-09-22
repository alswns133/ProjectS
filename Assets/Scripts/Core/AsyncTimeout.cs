using System;
using System.Threading.Tasks;
using UnityEngine;

namespace ProjectS.Core
{
    /// <summary>
    /// "언제 끝날지 보장되지 않는 Task"를 기다릴 때 쓰는 타임아웃 헬퍼.
    ///
    /// 진입 화면(로그인 · 캐릭터 선택)이 Firebase 초기화를 기다리는 동안 화면은 베일로 덮여 있다.
    /// 네트워크가 막혀 초기화가 끝나지 않으면 사용자는 로고 화면에 <b>영영 갇힌다</b> — 그 사고를
    /// 막기 위해 진입 경로의 모든 대기에는 반드시 이 헬퍼를 끼운다.
    /// </summary>
    public static class AsyncTimeout
    {
        /// <summary>
        /// <paramref name="task"/>가 <paramref name="seconds"/> 안에 끝나기를 기다린다.
        /// 시간이 지나도 Task 자체는 취소되지 않는다(계속 돌다 나중에 끝날 수 있다) — 호출부가
        /// "기다리기를 포기할 뿐"이라는 뜻이다.
        /// </summary>
        /// <param name="task">기다릴 작업</param>
        /// <param name="seconds">최대 대기 시간(초). 0 이하면 타임아웃 없이 끝까지 기다린다</param>
        /// <returns>제 시간 안에 끝났으면 true, 시간이 다 됐으면 false</returns>
        public static async Task<bool> Wait(Task task, float seconds)
        {
            if (task == null) return true;

            if (seconds <= 0f)
            {
                await task;
                return true;
            }

            Task finished = await Task.WhenAny(task, Task.Delay(TimeSpan.FromSeconds(seconds)));
            if (finished != task) return false;

            // 완료된 Task를 한 번 더 await해 안에서 터진 예외를 여기로 끌어올린다(조용히 삼키지 않게).
            try
            {
                await task;
            }
            catch (Exception e)
            {
                Debug.LogError($"[AsyncTimeout] 대기하던 작업이 예외로 끝났습니다: {e}");
            }

            return true;
        }
    }
}
