#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
using System;
using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

// The presentation assembly has no automatic reference from Assembly-CSharp.
[assembly: UnityEngine.Scripting.AlwaysLinkAssembly]

namespace Redbool.DesktopPresentation
{
    public sealed class DesktopCaptionBootstrap : MonoBehaviour
    {
        static DesktopCaptionBootstrap instance;
        Coroutine pending;
        bool warned;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetSession() { instance = null; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Create()
        {
            if (Application.isBatchMode || instance != null) return;
            GameObject owner = new GameObject("Desktop presentation");
            DontDestroyOnLoad(owner);
            instance = owner.AddComponent<DesktopCaptionBootstrap>();
            instance.RequestCaption();
        }

        void OnEnable() { SceneManager.sceneLoaded += SceneLoaded; }
        void OnDisable()
        {
            SceneManager.sceneLoaded -= SceneLoaded;
            if (pending != null) StopCoroutine(pending);
            pending = null;
        }
        void OnDestroy() { if (instance == this) instance = null; }
        void SceneLoaded(Scene scene, LoadSceneMode mode) { RequestCaption(); }
        void OnApplicationFocus(bool focused) { if (focused) RequestCaption(); }

        void RequestCaption()
        {
            if (!isActiveAndEnabled) return;
            if (pending != null) StopCoroutine(pending);
            pending = StartCoroutine(ApplyBounded());
        }

        IEnumerator ApplyBounded()
        {
            // Continue the small burst after success to cover native window recreation.
            // There is no Update loop, timer, worker thread, save access, or game dependency.
            var delay = new WaitForSecondsRealtime(0.25f);
            bool verified = false;
            for (int attempt = 0; attempt < 8; attempt++)
            {
                try { verified |= WindowsCaption.ApplyToCurrentProcess().Success; }
                catch (Exception exception)
                {
                    if (!warned) Debug.LogWarning("Desktop caption unavailable: " + exception.GetType().Name);
                    warned = true;
                    break;
                }
                yield return delay;
            }
            if (!verified && !warned)
            {
                Debug.LogWarning("Desktop caption: no matching window was verified.");
                warned = true;
            }
            pending = null;
        }
    }
}
#endif
