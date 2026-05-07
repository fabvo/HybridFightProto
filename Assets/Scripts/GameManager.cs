using System.Collections;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
using UnityEngine.InputSystem;
#endif

public enum PlayerMode { Idle, Blocking, AttackReady, Focusing }

/// <summary>
/// Core game logic. All tag effects (Focus, Heal, Shield, Bomb) are applied in
/// a single UpdateTagEffects() method that works identically in Solo and Multiplayer.
/// NetworkController.SendAttack/SendHp already handle both modes internally, so
/// GameManager never branches on IsSolo.
///
/// Tag effects:
///   Focus  (default): charges next attack while on card
///   Heal:             restores HP over time while on card
///   Shield:           incoming damage = 0 while on card
///   Bomb:             fires instant damage to opponent on first contact
/// </summary>
public class GameManager : MonoBehaviour
{
    public PlayerMode Mode { get; private set; } = PlayerMode.Idle;

    // Tunables
    public const int   MaxHp              = 100;
    public const int   MaxCharge          = 50;
    public const int   BaseAttackDamage   = 10;
    public const int   BlockedDamageTaken = 2;
    public const float ChargePerSecond    = 18f;
    const   float SwipePixelThreshold     = 150f;
    const   float AttackCooldown          = 0.6f;
    public const float SoloRespawnSeconds = 2f;

    public int  MyHp       { get; private set; } = MaxHp;
    public int  OpponentHp { get; private set; } = MaxHp;
    public int  Charge     { get; private set; } = 0;
    public bool GameOver   { get; private set; }

    // Swipe visualization
    public bool    SwipeActive  { get; private set; }
    public Vector2 SwipeStart   { get; private set; }
    public Vector2 SwipeCurrent { get; private set; }

    public event System.Action<PlayerMode> OnModeChanged;
    public event System.Action OnHpChanged;
    public event System.Action OnChargeChanged;
    public event System.Action OnGameOver;
    public event System.Action<int> OnAttackFired;

    TiltDetector       tilt;
    NfcManager         nfc;
    NetworkController  net;

    float lastAttackTime;
    bool  soloRespawningPlayer;
    bool  bombFiredThisPlacement;    // prevents Bomb from firing every frame

    void Start()
    {
        tilt = GetComponent<TiltDetector>();
        net  = GetComponent<NetworkController>();
        nfc  = GameObject.Find("NfcManager").GetComponent<NfcManager>();

        net.OnAttackReceived    += HandleIncomingAttack;
        net.OnOpponentHpUpdated += hp => { OpponentHp = hp; OnHpChanged?.Invoke(); CheckGameOver(); };
        net.OnConnected         += ResetMatch;

#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
        if (Touchscreen.current != null && !Touchscreen.current.enabled)
            InputSystem.EnableDevice(Touchscreen.current);
#endif
    }

    void Update()
    {
        if (!net.IsConnected || GameOver) return;
        UpdateMode();
        UpdateTagEffects();
        DetectSwipe();
    }

    // ===================== Mode resolution =====================
    // Priority: Block > Focus (any tag present) > AttackReady > Idle
    void UpdateMode()
    {
        PlayerMode next;
        if (tilt.State == TiltState.Block)
            next = PlayerMode.Blocking;
        else if (nfc.TagPresent)
            next = PlayerMode.Focusing;
        else if (tilt.State == TiltState.AttackReady)
            next = PlayerMode.AttackReady;
        else
            next = PlayerMode.Idle;

        if (next != Mode)
        {
            Mode = next;
            OnModeChanged?.Invoke(Mode);
            net.SendMode(Mode);
        }
    }

    // ===================== Tag effects (central, mode-agnostic) =====================
    // This is the SINGLE place where all NFC tag effects are applied.
    // It uses net.SendAttack / net.SendHp which handle Solo + Multiplayer internally.
    void UpdateTagEffects()
    {
        if (!nfc.TagPresent)
        {
            bombFiredThisPlacement = false;
            return;
        }

        switch (nfc.CurrentEffect)
        {
            case TagEffect.Focus:
                UpdateCharge();
                break;

            case TagEffect.Heal:
                int prevHp = MyHp;
                MyHp = Mathf.Min(MaxHp, MyHp + Mathf.RoundToInt(TagEffects.HealHpPerSecond * Time.deltaTime));
                if (MyHp != prevHp) { OnHpChanged?.Invoke(); net.SendHp(MyHp); }
                break;

            case TagEffect.Shield:
                // Passive: damage reduction is handled in HandleIncomingAttack.
                break;

            case TagEffect.Bomb:
                if (!bombFiredThisPlacement)
                {
                    bombFiredThisPlacement = true;
                    net.SendAttack(TagEffects.BombDamage);
                    OnAttackFired?.Invoke(TagEffects.BombDamage);
                }
                break;
        }
    }

    void UpdateCharge()
    {
        if (Mode != PlayerMode.Focusing) return;
        int prev = Charge;
        Charge = Mathf.Min(MaxCharge, Charge + Mathf.RoundToInt(ChargePerSecond * Time.deltaTime));
        if (Charge != prev) OnChargeChanged?.Invoke();
    }

    // ===================== Swipe-to-attack =====================
    void DetectSwipe()
    {
        if (Mode == PlayerMode.Focusing) { SwipeActive = false; return; }
        if (Mode != PlayerMode.AttackReady) { SwipeActive = false; return; }

        Vector2 pos;
        bool    began, ended;
        if (!ReadPrimaryTouch(out pos, out began, out ended))
        {
            if (!Input_TouchActive()) SwipeActive = false;
            return;
        }

        if (began)
        {
            SwipeStart   = pos;
            SwipeCurrent = pos;
            SwipeActive  = true;
        }
        else if (ended && SwipeActive)
        {
            SwipeActive = false;
            SwipeCurrent = pos;
            float distance = (pos - SwipeStart).magnitude;
            if (distance > SwipePixelThreshold && Time.time - lastAttackTime > AttackCooldown)
                FireAttack();
        }
        else
        {
            SwipeCurrent = pos;
        }
    }

    bool Input_TouchActive()
    {
#if ENABLE_LEGACY_INPUT_MANAGER
        return Input.touchCount > 0;
#elif ENABLE_INPUT_SYSTEM
        return Touchscreen.current != null
            && Touchscreen.current.primaryTouch.phase.ReadValue() != UnityEngine.InputSystem.TouchPhase.None;
#else
        return false;
#endif
    }

    bool ReadPrimaryTouch(out Vector2 position, out bool began, out bool ended)
    {
#if ENABLE_LEGACY_INPUT_MANAGER
        if (Input.touchCount == 0) { position = default; began = ended = false; return false; }
        var t = Input.GetTouch(0);
        position = t.position;
        began = t.phase == TouchPhase.Began;
        ended = t.phase == TouchPhase.Ended || t.phase == TouchPhase.Canceled;
        return true;
#elif ENABLE_INPUT_SYSTEM
        var ts = Touchscreen.current;
        if (ts == null) { position = default; began = ended = false; return false; }
        var pt = ts.primaryTouch;
        var phase = pt.phase.ReadValue();
        if (phase == UnityEngine.InputSystem.TouchPhase.None) { position = default; began = ended = false; return false; }
        position = pt.position.ReadValue();
        began = phase == UnityEngine.InputSystem.TouchPhase.Began;
        ended = phase == UnityEngine.InputSystem.TouchPhase.Ended || phase == UnityEngine.InputSystem.TouchPhase.Canceled;
        return true;
#else
        position = default; began = ended = false; return false;
#endif
    }

    void FireAttack()
    {
        int damage = BaseAttackDamage + Charge;
        Charge = 0;
        OnChargeChanged?.Invoke();
        lastAttackTime = Time.time;
        net.SendAttack(damage);
        OnAttackFired?.Invoke(damage);
    }

    // ===================== Damage taken =====================
    void HandleIncomingAttack(int incoming)
    {
        if (soloRespawningPlayer) return;

        int actual;
        if (Mode == PlayerMode.Blocking)
            actual = BlockedDamageTaken;
        else if (nfc.TagPresent && nfc.CurrentEffect == TagEffect.Shield)
            actual = 0;    // Shield card absorbs ALL damage while on it
        else
            actual = incoming;

        MyHp = Mathf.Max(0, MyHp - actual);
        OnHpChanged?.Invoke();
        net.SendHp(MyHp);

        if (MyHp <= 0)
        {
            if (net.IsSolo) StartCoroutine(SoloRespawnPlayer());
            else            CheckGameOver();
        }
    }

    IEnumerator SoloRespawnPlayer()
    {
        soloRespawningPlayer = true;
        yield return new WaitForSeconds(SoloRespawnSeconds);
        MyHp   = MaxHp;
        Charge = 0;
        soloRespawningPlayer = false;
        OnHpChanged?.Invoke();
        OnChargeChanged?.Invoke();
    }

    void CheckGameOver()
    {
        if (GameOver || net.IsSolo) return;
        if (MyHp <= 0 || OpponentHp <= 0)
        {
            GameOver = true;
            OnGameOver?.Invoke();
        }
    }

    public void ResetMatch()
    {
        MyHp = OpponentHp = MaxHp;
        Charge = 0;
        GameOver = false;
        bombFiredThisPlacement = false;
        OnHpChanged?.Invoke();
        OnChargeChanged?.Invoke();
    }
}
