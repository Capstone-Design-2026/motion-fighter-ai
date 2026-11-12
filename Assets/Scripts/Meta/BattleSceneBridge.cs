using System.Collections;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// GameScene 과 지도 진행을 이어주는 다리이자, K.O. 연출 담당.
///
/// 전투 시작 전 : 지도에서 고른 스테이지 정보(적 이름/체력/프리팹)와 이어받은 플레이어 체력을 반영한다.
/// K.O. 순간   : 양쪽 조작을 잠그고 짧게 슬로우모션을 건다. (시간을 멈추지는 않는다)
/// 전투 종료 후 : 승패를 RunState 에 알리고 지도(또는 시작 화면)로 돌려보낸다.
///
/// runMode 구분:
///   지도를 거쳐 들어온 전투  → 체력 이어받기 + R 재시작 차단 + 씬 전환까지 전부 담당
///   GameScene 단독 실행      → K.O. 연출만 하고, 재시작은 예전처럼 CombatHUD 의 R 키에 맡긴다
///
/// 실행 순서를 -500 으로 당겨둔 이유:
/// Health.Awake 와 CombatHUD.Awake 보다 먼저 최대 체력을 바꿔야 HUD 에 처음부터 올바른 값이 뜬다.
/// </summary>
[DefaultExecutionOrder(-500)]
public class BattleSceneBridge : MonoBehaviour
{
    [Header("제한 시간")]
    [Min(1f)]
    [SerializeField] private float roundDuration = 120f;
    public float RemainingSeconds { get; private set; }
    public bool IsResolved => resolved;

    [Header("전투 종료 후")]
    [Tooltip("K.O. 뒤 지도로 돌아가기까지 기다리는 시간(초). 실제 시간 기준이라 슬로우모션의 영향을 받지 않는다.")]
    [SerializeField] private float returnDelay = 2.5f;

    [Header("K.O. 연출")]
    [Tooltip("마무리 타격 직후 잠깐 느려진다. 시간을 멈추는 게 아니라 늦추는 것이라 쓰러지는 모션이 끝까지 보인다.")]
    [SerializeField] private bool useSlowMotion = true;
    [Range(0.05f, 1f)]
    [SerializeField] private float slowMotionScale = 0.22f;
    [Tooltip("느려진 상태로 붙잡아 두는 시간. 기본 타격의 슬로우보다 길어야 마무리가 마무리로 읽힌다.")]
    [SerializeField] private float slowMotionDuration = 0.85f;
    [Tooltip("정속으로 돌아오는 시간. 0 이면 뚝 끊긴다.")]
    [SerializeField] private float slowMotionRecoverTime = 0.45f;

    private Health playerHealth;
    private Health enemyHealth;
    private Fighter[] fighters;
    private Text bannerText;

    private RedboolBattlePreparation visionPreparation;
    private bool resolved;
    private bool runMode;                 // 지도를 거쳐 들어온 전투인가
    private float defaultFixedDelta;

    private RunState Run { get { return RunState.Instance; } }

    void Awake()
    {
        defaultFixedDelta = Time.fixedDeltaTime;
        if (RedboolPvp.Active) { enabled=false; return; }
        RemainingSeconds = Mathf.Max(1f, roundDuration);

        runMode = !RedboolDuelTest.Active && Run != null && Run.HasPendingBattle;
        if (runMode) ApplyStage();
    }

    void Start()
    {
        foreach (var gate in FindObjectsByType<RedboolBattlePreparation>(FindObjectsSortMode.None))
            if (gate.gameObject.scene == gameObject.scene) { visionPreparation = gate; break; }
        var player = FindAnyObjectByType<PlayerController>();
        if (player != null) playerHealth = player.GetComponent<Health>();

        var enemy = FindAnyObjectByType<EnemyAI>();
        if (enemy != null) enemyHealth = enemy.GetComponent<Health>();

        // 스테이지 프리팹으로 적을 교체했을 수도 있으므로 Awake 가 아니라 여기서 모은다.
        fighters = FindObjectsByType<Fighter>();

        if (playerHealth != null) playerHealth.OnDeath += OnAnyDeath;
        if (enemyHealth  != null) enemyHealth.OnDeath  += OnAnyDeath;

        if (!runMode) return;

        // 이어받은 체력 반영. Health.Awake 가 끝난 뒤여야 하므로 Start 에서 한다.
        if (playerHealth != null)
        {
            playerHealth.SetCurrentHealth(Run.Config.carryPlayerHealth
                ? Run.Data.playerHealth
                : Run.Data.playerMaxHealth);
        }

        // 지도를 진행 중일 때는 R 키 재시작을 막는다.
        // 이기면 자동으로 지도로 돌아가고, 지면 판이 끝나야 하므로 되살아나면 안 된다.
        var hud = FindAnyObjectByType<CombatHUD>();
        if (hud != null) hud.AllowRestart = false;

        BuildBanner();
    }

    void Update()
    {
        if (resolved || playerHealth == null || enemyHealth == null) return;
        if (visionPreparation != null && visionPreparation.HoldRound) return;
        if (playerHealth.IsDead || enemyHealth.IsDead) return;

        // 게임 시간 기준: 일시정지/필살기 정지 중에는 제한 시간도 멈춘다.
        RemainingSeconds = Mathf.Max(0f, RemainingSeconds - Time.deltaTime);
        if (RemainingSeconds > 0f) return;

        // 최대 HP가 다른 보스도 공정하게 비교한다. 같은 비율이면 플레이어 패배.
        bool won = (long)playerHealth.CurrentHealth * enemyHealth.MaxHealth
                 > (long)enemyHealth.CurrentHealth * playerHealth.MaxHealth;
        ResolveResult(won, true);
    }

    public void ResetRoundTimer()
    {
        if (runMode) return;
        StopAllCoroutines();
        HitImpact.ReleaseTimeControl();
        RestoreTimeScale();
        resolved = false;
        RemainingSeconds = Mathf.Max(1f, roundDuration);
        SetInputLock(false);
    }

    void OnDestroy()
    {
        // 씬을 넘기기 전에 반드시 원복. 안 하면 다음 씬이 슬로우모션 상태로 시작한다.
        RestoreTimeScale();

        if (playerHealth != null) playerHealth.OnDeath -= OnAnyDeath;
        if (enemyHealth  != null) enemyHealth.OnDeath  -= OnAnyDeath;
    }

    // ─────────────────────── 전투 준비 ───────────────────────

    /// <summary>스테이지 정의를 씬에 반영한다. 정의가 없으면 씬의 기본 적을 그대로 쓴다.</summary>
    private void ApplyStage()
    {
        var stage = Run.PendingStage;
        var node  = Run.PendingNode;

        if (stage == null)
        {
            string what = node != null ? node.type.ToString() : "?";
            Debug.Log("[BattleSceneBridge] 이 칸(" + what + ")에 배정된 스테이지가 없어 GameScene 기본 적으로 싸웁니다.");
            return;
        }

        var enemy = FindAnyObjectByType<EnemyAI>();

        // 잘못된 프리팹이면 기존 적을 보존한다. 전투 컴포넌트는 루트에 둔다.
        if (stage.enemyPrefab != null)
        {
            var prefab = stage.enemyPrefab;
            bool valid = prefab.activeSelf && prefab.GetComponent<EnemyAI>() != null
                && prefab.GetComponent<EnemyAI>().enabled && prefab.GetComponent<Health>() != null
                && prefab.GetComponent<Fighter>() != null && prefab.GetComponent<Rigidbody2D>() != null
                && prefab.GetComponent<Collider2D>() != null && prefab.GetComponent<Pushbox>() != null;
            if (!valid)
                Debug.LogError("[BattleSceneBridge] 보스 프리팹에 활성 EnemyAI, Health, Fighter, Rigidbody2D, Collider2D, Pushbox가 필요합니다: " + stage.id);
            else
            {
                Vector3 pos = enemy != null ? enemy.transform.position : new Vector3(3f, 0f, 0f);
                Quaternion rot = enemy != null ? enemy.transform.rotation : Quaternion.identity;
                if (enemy != null)
                {
                    enemy.gameObject.SetActive(false);
                    Destroy(enemy.gameObject);
                }
                var spawned = Instantiate(prefab, pos, rot);
                spawned.name = "Enemy (" + stage.id + ")";
                enemy = spawned.GetComponent<EnemyAI>();
            }
        }
        if (enemy == null) return;

        var health = enemy.GetComponent<Health>();
        if (health != null)
        {
            health.Configure(stage.displayName, stage.enemyMaxHealth);
            if (stage.overrideTint) health.SetAppearance(stage.enemyTint);
        }
        enemy.SetBehavior(stage.behaviorType);
        if (stage.overrideCombatStats)
        {
            enemy.ConfigureStage(stage);
            enemy.GetComponent<Fighter>().ConfigureDamage(stage.attackDamage);
        }
        var animator = enemy.GetComponent<Animator>();
        if (animator != null && stage.animatorController != null)
            animator.runtimeAnimatorController = stage.animatorController;

        // A node can own a distinct model even when several nodes share combat stats.
        var visualPrefab = Run.IsStoryMap && node != null ? Run.Config.GetEnemyVisual(node.id) : null;
        var view = enemy.GetComponent<Fighter3DView>();
        if (visualPrefab != null && view != null && view.ApplyVisual(visualPrefab))
        {
            // Preserve the authored texture colours instead of the placeholder boss tint.
            if (health != null) health.SetAppearance(Color.white);
        }

        var player = FindAnyObjectByType<PlayerController>();
        if (player != null)
        {
            player.SetOpponent(enemy.transform);
            var playerPushbox = player.GetComponent<Pushbox>();
            var enemyPushbox = enemy.GetComponent<Pushbox>();
            if (playerPushbox != null) playerPushbox.SetOpponent(enemyPushbox);
            if (enemyPushbox != null) enemyPushbox.SetOpponent(playerPushbox);
        }
        var hud = FindAnyObjectByType<CombatHUD>();
        if (hud != null) hud.SetTargets(player != null ? player.GetComponent<Health>() : null, health);

    }

    // ─────────────────────── K.O. ───────────────────────

    private void OnAnyDeath(Health dead)
    {
        ResolveResult(dead != playerHealth, false);
    }

    private void ResolveResult(bool won, bool timedOut)
    {
        if (resolved) return;
        resolved = true;
        SetInputLock(true);
        if (timedOut)
        {
            var hud = FindAnyObjectByType<CombatHUD>();
            if (hud != null) hud.ShowTimeOut(won);
        }
        StartCoroutine(FinishRoutine(won, timedOut));
    }

    private IEnumerator FinishRoutine(bool won, bool timedOut)
    {
        // 타격 히트스톱이 아직 돌고 있으면 여기서 정리한다.
        // 지금부터 timeScale 은 K.O. 연출이 혼자 쓴다. 둘이 겹치면 서로를 덮어써서 연출이 끊긴다.
        HitImpact.ReleaseTimeControl();
        if (timedOut) RestoreTimeScale();

        // 이긴 쪽까지 포함해 양쪽 조작을 잠근다. 쓰러진 상대를 계속 때리는 걸 막는다.
        SetInputLock(true);

        // 마무리 일격을 눈에 남기기 위한 슬로우모션. 기본 타격의 슬로우보다 길게 간다.
        // timeScale 을 0 으로 만들지 않는 이유: 쓰러지는 모션과 넉백이 첫 프레임에서 얼어붙는다.
        if (!timedOut && useSlowMotion && slowMotionDuration > 0f)
        {
            SetTimeScale(slowMotionScale);

            yield return new WaitForSecondsRealtime(slowMotionDuration);

            // 정속으로 부드럽게 돌아온다. 길게 늦춘 뒤 뚝 끊으면 눈에 확 걸린다.
            float t = 0f;
            while (t < slowMotionRecoverTime)
            {
                t += Time.unscaledDeltaTime;
                SetTimeScale(Mathf.Lerp(slowMotionScale, 1f, t / slowMotionRecoverTime));
                yield return null;
            }

            RestoreTimeScale();
        }

        // 단독 실행이면 연출까지만. 재시작은 CombatHUD 의 R 키가 맡는다.
        if (!runMode) yield break;

        if (bannerText != null)
        {
            bannerText.text  = won ? (Run.PendingNode != null && Run.PendingNode.type == NodeType.Boss
                ? "최종 보스 격파 - 엔딩으로 이동합니다" : "승리 - 지도로 돌아갑니다") : "패배 - 이 판이 끝납니다";
            bannerText.color = won ? new Color(1f, 0.9f, 0.4f) : new Color(1f, 0.5f, 0.5f);
            bannerText.gameObject.SetActive(true);
        }

        yield return new WaitForSecondsRealtime(returnDelay);

        RestoreTimeScale();

        int remaining = playerHealth != null ? playerHealth.CurrentHealth : 0;
        Run.ResolveBattle(won, remaining);
    }

    private void SetInputLock(bool locked)
    {
        if (playerHealth != null) playerHealth.DamageLocked = locked;
        if (enemyHealth != null) enemyHealth.DamageLocked = locked;
        if (fighters == null) return;
        for (int i = 0; i < fighters.Length; i++)
            if (fighters[i] != null) fighters[i].InputLocked = locked;
    }

    /// <summary>물리 간격도 같이 줄여야 느려지는 동안 움직임이 뚝뚝 끊기지 않는다.</summary>
    private void SetTimeScale(float scale)
    {
        Time.timeScale = scale;
        Time.fixedDeltaTime = defaultFixedDelta * scale;
    }

    private void RestoreTimeScale()
    {
        Time.timeScale = 1f;
        Time.fixedDeltaTime = defaultFixedDelta > 0f ? defaultFixedDelta : 0.02f;
    }

    private void BuildBanner()
    {
        // CombatHUD 의 K.O. 문구(sortingOrder 100) 보다 위에 뜨도록 한다.
        var canvas = UIFactory.CreateCanvas("BattleResultCanvas", transform, 200);
        bannerText = UIFactory.CreateText("ResultText", canvas.transform, "", 46, TextAnchor.MiddleCenter);
        UIFactory.Place(bannerText.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                        new Vector2(0f, -140f), new Vector2(1400f, 100f));
        bannerText.gameObject.SetActive(false);
    }
}
