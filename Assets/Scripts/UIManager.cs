using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
using UnityEngine.InputSystem.UI;
#endif

/// <summary>
/// Builds the entire UI in code so you don't have to wire anything up in the Unity scene.
///
/// NFC status badge has three visual states (driven by ReaderMode polling):
///   - Red    "noch kein Tag erkannt"           : nothing seen yet this session
///   - Green  "TAG LIEGT DRAUF  UID:XX"         : tag is currently in field (events <0.6s old)
///   - Gray   "letzter Tag UID:XX vor 1.4s"     : tag was lifted, waiting for next contact
///
/// While TAG LIEGT DRAUF the badge also gently pulses to make the live state obvious.
/// </summary>
public class UIManager : MonoBehaviour
{
    GameManager       game;
    NetworkController net;
    NfcManager        nfc;
    Canvas            canvas;
    RectTransform     canvasRect;

    GameObject lobbyPanel, gamePanel;
    InputField ipInput;
    Text statusText, modeText, myHpText, oppHpText, gameOverText, hintText, soloBadge, nfcStatusText;
    Image myHpBar, oppHpBar, chargeBar;
    Image nfcStatusBg, nfcFlash, attackFlash;
    Image swipeLine;
    Text damagePopup;

    Sprite uiSprite;
    Color  chargeBarBaseColor   = new Color(1f, 0.85f, 0.20f);
    Color  chargeBarPulseColor  = new Color(1f, 1.0f,  0.55f);

    void Start()
    {
        game = GetComponent<GameManager>();
        net  = GetComponent<NetworkController>();
        nfc  = GameObject.Find("NfcManager").GetComponent<NfcManager>();

        uiSprite = Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f));

        BuildUi();

        net.OnConnected     += OnConnected;
        net.OnDisconnected  += () => { lobbyPanel.SetActive(true);  gamePanel.SetActive(false); };
        game.OnHpChanged    += UpdateHp;
        game.OnChargeChanged+= UpdateCharge;
        game.OnModeChanged  += UpdateMode;
        game.OnGameOver     += ShowGameOver;
        game.OnAttackFired  += OnAttackFired;
        nfc .OnTagDiscovered+= OnNfcDiscovered;
        nfc .OnPluginError  += OnNfcError;

        UpdateHp();
        UpdateCharge();
        UpdateMode(PlayerMode.Idle);
        UpdateNfcStatus();
    }

    void OnConnected()
    {
        lobbyPanel.SetActive(false);
        gamePanel.SetActive(true);
        oppHpText.text = (net.IsSolo ? "Trainer: " : "Gegner: ") + game.OpponentHp;
        soloBadge.gameObject.SetActive(net.IsSolo);
        UpdateNfcStatus();
    }

    void Update()
    {
        statusText.text = $"{net.Status}\nMeine IP: {net.LocalIp}";
        UpdateNfcStatus();
        UpdateSwipeTrail();
        UpdateChargePulse();
    }

    // ===================== NFC status & flash ==============================
    void UpdateNfcStatus()
    {
        if (nfc == null) return;

        if (nfc.LastTagId == null)
        {
            nfcStatusText.text = "NFC: noch kein Tag erkannt";
            nfcStatusBg .color = new Color(0.45f, 0.18f, 0.18f, 1f);
        }
        else if (nfc.TagPresent)
        {
            // Live, tag is on the phone right now. Slow gentle pulse so you can see updates flowing.
            float pulse = 0.5f + 0.5f * Mathf.Sin(Time.time * 5f);
            nfcStatusText.text = $"TAG LIEGT DRAUF  UID:{nfc.LastTagId}";
            nfcStatusBg .color = Color.Lerp(new Color(0.18f, 0.55f, 0.22f, 1f),
                                            new Color(0.30f, 0.80f, 0.35f, 1f), pulse);
        }
        else
        {
            float since = nfc.SecondsSinceLastTag;
            nfcStatusText.text = $"letzter Tag #{nfc.TagsDetectedCount}  UID:{nfc.LastTagId}  vor {since:0.0}s";
            nfcStatusBg .color = new Color(0.30f, 0.32f, 0.36f, 1f);
        }
    }

    void OnNfcDiscovered(string uid)
    {
        // Only flash on the first event of a contact, not on every poll while it's lying there.
        // We approximate "first event" by checking that the tag was NOT present an instant ago.
        // The NfcManager increments TagsDetectedCount only on first contact -- piggyback on that.
        if (lastSeenContactCount != nfc.TagsDetectedCount)
        {
            lastSeenContactCount = nfc.TagsDetectedCount;
            StartCoroutine(FlashOverlay(nfcFlash, new Color(1f, 0.85f, 0.25f, 0.65f), 0.45f));
        }
    }
    int lastSeenContactCount;

    void OnNfcError(string err)
    {
        nfcStatusBg.color = new Color(0.55f, 0.10f, 0.10f, 1f);
        nfcStatusText.text = "NFC: " + err;
    }

    // ===================== Swipe trail =====================================
    void UpdateSwipeTrail()
    {
        if (game == null || !game.SwipeActive)
        {
            if (swipeLine.gameObject.activeSelf) swipeLine.gameObject.SetActive(false);
            return;
        }

        if (!swipeLine.gameObject.activeSelf) swipeLine.gameObject.SetActive(true);

        Vector2 a, b;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, game.SwipeStart,   null, out a);
        RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, game.SwipeCurrent, null, out b);

        Vector2 mid     = (a + b) * 0.5f;
        Vector2 delta   = b - a;
        float distance  = delta.magnitude;
        float angleDeg  = Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg;

        var rt = swipeLine.rectTransform;
        rt.anchoredPosition  = mid;
        rt.sizeDelta         = new Vector2(distance, 14f);
        rt.localEulerAngles  = new Vector3(0, 0, angleDeg);

        float t = Mathf.Clamp01(distance / 600f);
        swipeLine.color = Color.Lerp(new Color(1f, 1f, 1f, 0.55f),
                                     new Color(1f, 0.4f, 0.2f, 0.95f), t);
    }

    // ===================== Charge bar pulse during focus ==================
    void UpdateChargePulse()
    {
        if (chargeBar == null) return;
        if (game != null && game.Mode == PlayerMode.Focusing)
        {
            float pulse = 0.5f + 0.5f * Mathf.Sin(Time.time * 6f);
            chargeBar.color = Color.Lerp(chargeBarBaseColor, chargeBarPulseColor, pulse);
        }
        else
        {
            chargeBar.color = chargeBarBaseColor;
        }
    }

    // ===================== Attack feedback =================================
    void OnAttackFired(int damage)
    {
        StartCoroutine(FlashOverlay(attackFlash, new Color(1f, 0.45f, 0.15f, 0.55f), 0.35f));
        StartCoroutine(ShowDamagePopup(damage));
    }

    IEnumerator ShowDamagePopup(int damage)
    {
        damagePopup.text = $"+{damage} SCHADEN!";
        damagePopup.gameObject.SetActive(true);
        var rt = damagePopup.rectTransform;
        Vector2 startPos = new Vector2(0, 100);
        Vector2 endPos   = new Vector2(0, 400);

        float duration = 0.85f;
        float t = 0;
        while (t < duration)
        {
            t += Time.deltaTime;
            float p = t / duration;
            rt.anchoredPosition = Vector2.Lerp(startPos, endPos, p);
            var c = damagePopup.color;
            c.a = 1f - p;
            damagePopup.color = c;
            yield return null;
        }
        damagePopup.gameObject.SetActive(false);
        damagePopup.color = new Color(1f, 0.5f, 0.2f, 1f);
    }

    IEnumerator FlashOverlay(Image overlay, Color startColor, float duration)
    {
        overlay.gameObject.SetActive(true);
        float t = 0;
        while (t < duration)
        {
            t += Time.deltaTime;
            float p = t / duration;
            var c = startColor;
            c.a = startColor.a * (1f - p);
            overlay.color = c;
            yield return null;
        }
        overlay.gameObject.SetActive(false);
    }

    // -----------------------------------------------------------------------
    void BuildUi()
    {
        var canvasGo = new GameObject("Canvas");
        canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1080, 1920);
        scaler.matchWidthOrHeight = 0.5f;
        canvasGo.AddComponent<GraphicRaycaster>();
        canvasRect = canvasGo.GetComponent<RectTransform>();

        var es = new GameObject("EventSystem");
        es.AddComponent<EventSystem>();
#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
        es.AddComponent<InputSystemUIInputModule>();
#else
        es.AddComponent<StandaloneInputModule>();
#endif

        // ===================== Lobby =====================
        lobbyPanel = MakePanel("LobbyPanel", canvasGo.transform, new Color(0.08f, 0.10f, 0.16f));

        var title = MakeText(lobbyPanel.transform, "HYBRID FIGHT", new Vector2(0, 760), 90);
        title.fontStyle = FontStyle.Bold;
        title.color = new Color(0.7f, 1f, 0.7f);

        MakeText(lobbyPanel.transform, "Prototype – beide Geraete ins gleiche WLAN!", new Vector2(0, 660), 30);
        MakeText(lobbyPanel.transform,
            "Zwei Spieler:  Spieler 1 drueckt HOST, Spieler 2 tippt die IP ein und JOIN.\n"
          + "Allein testen: SOLO druecken, KI greift dich an.",
            new Vector2(0, 470), 28);

        ipInput = MakeInputField(lobbyPanel.transform, "z.B. 192.168.1.42", new Vector2(0, 230));

        MakeButton(lobbyPanel.transform, "JOIN",      new Vector2(0,  100), new Color(0.30f, 0.55f, 0.85f), () => net.Join(ipInput.text));
        MakeButton(lobbyPanel.transform, "HOST",      new Vector2(0,  -50), new Color(0.85f, 0.45f, 0.30f), () => net.StartHost());
        MakeButton(lobbyPanel.transform, "SOLO TEST", new Vector2(0, -200), new Color(0.40f, 0.70f, 0.45f), () => net.StartSolo());

        statusText = MakeText(lobbyPanel.transform, "Nicht verbunden", new Vector2(0, -500), 30);
        statusText.color = new Color(0.7f, 0.7f, 0.8f);

        // ===================== Game =====================
        gamePanel = MakePanel("GamePanel", canvasGo.transform, new Color(0.05f, 0.06f, 0.10f));
        gamePanel.SetActive(false);

        oppHpText = MakeText(gamePanel.transform, "Gegner: 100", new Vector2(0, 850), 40);
        oppHpBar  = MakeBar (gamePanel.transform, new Vector2(0, 780), new Color(0.9f, 0.3f, 0.3f), 800f, 35f);

        soloBadge = MakeText(gamePanel.transform, "[ SOLO TEST ]", new Vector2(0, 700), 28);
        soloBadge.color = new Color(0.55f, 0.85f, 0.55f);
        soloBadge.gameObject.SetActive(false);

        // NFC status badge
        var nfcGo = new GameObject("NfcStatus");
        nfcGo.transform.SetParent(gamePanel.transform, false);
        var nfcRt = nfcGo.AddComponent<RectTransform>();
        nfcRt.sizeDelta = new Vector2(960, 70);
        nfcRt.anchoredPosition = new Vector2(0, 600);
        nfcStatusBg = nfcGo.AddComponent<Image>();
        nfcStatusBg.sprite = uiSprite;
        nfcStatusBg.color = new Color(0.45f, 0.18f, 0.18f, 1f);
        nfcStatusText = MakeText(nfcGo.transform, "NFC: noch kein Tag erkannt", Vector2.zero, 28);
        nfcStatusText.color = new Color(1f, 1f, 1f, 0.95f);

        // Mode indicator
        modeText = MakeText(gamePanel.transform, "-", new Vector2(0, 100), 90);
        modeText.fontStyle = FontStyle.Bold;

        hintText = MakeText(gamePanel.transform, "", new Vector2(0, -50), 32);
        hintText.color = new Color(0.7f, 0.7f, 0.8f);

        // Charge bar
        MakeText(gamePanel.transform, "AUFLADUNG", new Vector2(0, -550), 32);
        chargeBar = MakeBar(gamePanel.transform, new Vector2(0, -620), chargeBarBaseColor, 800f, 35f);

        // Self
        myHpText = MakeText(gamePanel.transform, "Du: 100", new Vector2(0, -760), 40);
        myHpBar  = MakeBar (gamePanel.transform, new Vector2(0, -830), new Color(0.3f, 0.85f, 0.4f), 800f, 35f);

        // Game-over overlay
        gameOverText = MakeText(gamePanel.transform, "", new Vector2(0, 350), 110);
        gameOverText.fontStyle = FontStyle.Bold;
        gameOverText.gameObject.SetActive(false);

        // ===================== Overlays =====
        var slGo = new GameObject("SwipeLine");
        slGo.transform.SetParent(gamePanel.transform, false);
        var slRt = slGo.AddComponent<RectTransform>();
        slRt.anchorMin = slRt.anchorMax = new Vector2(0.5f, 0.5f);
        slRt.pivot = new Vector2(0.5f, 0.5f);
        slRt.sizeDelta = new Vector2(0, 14);
        swipeLine = slGo.AddComponent<Image>();
        swipeLine.sprite = uiSprite;
        swipeLine.color = new Color(1f, 1f, 1f, 0.7f);
        swipeLine.raycastTarget = false;
        slGo.SetActive(false);

        damagePopup = MakeText(gamePanel.transform, "", new Vector2(0, 100), 84);
        damagePopup.fontStyle = FontStyle.Bold;
        damagePopup.color = new Color(1f, 0.55f, 0.2f, 1f);
        damagePopup.gameObject.SetActive(false);
        damagePopup.raycastTarget = false;

        nfcFlash    = MakeFullScreenOverlay(gamePanel.transform, "NfcFlash",    new Color(1f, 0.85f, 0.25f, 0f));
        attackFlash = MakeFullScreenOverlay(gamePanel.transform, "AttackFlash", new Color(1f, 0.45f, 0.15f, 0f));
    }

    Image MakeFullScreenOverlay(Transform parent, string name, Color color)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
        var img = go.AddComponent<Image>();
        img.sprite = uiSprite;
        img.color = color;
        img.raycastTarget = false;
        go.SetActive(false);
        return img;
    }

    void UpdateHp()
    {
        myHpText.text  = "Du: " + game.MyHp;
        oppHpText.text = (net != null && net.IsSolo ? "Trainer: " : "Gegner: ") + game.OpponentHp;
        myHpBar .fillAmount = game.MyHp       / (float)GameManager.MaxHp;
        oppHpBar.fillAmount = game.OpponentHp / (float)GameManager.MaxHp;
    }

    void UpdateCharge() => chargeBar.fillAmount = game.Charge / (float)GameManager.MaxCharge;

    void UpdateMode(PlayerMode m)
    {
        switch (m)
        {
            case PlayerMode.Blocking:
                modeText.text = "BLOCK";
                modeText.color = new Color(0.4f, 0.7f, 1f);
                hintText.text = "Halte das Handy aufrecht – du blockst.";
                break;
            case PlayerMode.AttackReady:
                modeText.text = "ANGRIFF";
                modeText.color = new Color(1f, 0.5f, 0.4f);
                hintText.text = "Wische ueber den Bildschirm um anzugreifen!";
                break;
            case PlayerMode.Focusing:
                modeText.text = "FOKUS";
                modeText.color = new Color(1f, 0.9f, 0.3f);
                hintText.text = "Lade auf... (nicht abheben!)";
                break;
            default:
                modeText.text = "BEREIT";
                modeText.color = new Color(0.6f, 0.6f, 0.7f);
                hintText.text = "Aufrecht = Block · Flach hoch = Angriff · Auf NFC-Karte = Fokus";
                break;
        }
    }

    void ShowGameOver()
    {
        bool won = game.MyHp > 0;
        gameOverText.text = won ? "GEWONNEN!" : "VERLOREN";
        gameOverText.color = won ? new Color(0.4f, 1f, 0.5f) : new Color(1f, 0.3f, 0.3f);
        gameOverText.gameObject.SetActive(true);
    }

    // ===== UGUI build helpers ==============================================
    GameObject MakePanel(string name, Transform parent, Color bg)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        var img = go.AddComponent<Image>();
        img.color = bg;
        img.sprite = uiSprite;
        return go;
    }

    Text MakeText(Transform parent, string content, Vector2 pos, int size)
    {
        var go = new GameObject("Text");
        go.transform.SetParent(parent, false);
        var rt = go.AddComponent<RectTransform>();
        rt.sizeDelta = new Vector2(1000, 140);
        rt.anchoredPosition = pos;
        var t = go.AddComponent<Text>();
        t.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        t.text = content;
        t.fontSize = size;
        t.alignment = TextAnchor.MiddleCenter;
        t.color = Color.white;
        t.horizontalOverflow = HorizontalWrapMode.Wrap;
        t.verticalOverflow   = VerticalWrapMode.Overflow;
        return t;
    }

    InputField MakeInputField(Transform parent, string placeholder, Vector2 pos)
    {
        var go = new GameObject("InputField");
        go.transform.SetParent(parent, false);
        var rt = go.AddComponent<RectTransform>();
        rt.sizeDelta = new Vector2(800, 110);
        rt.anchoredPosition = pos;
        var img = go.AddComponent<Image>();
        img.color = new Color(0.18f, 0.20f, 0.26f);
        img.sprite = uiSprite;

        var input = go.AddComponent<InputField>();
        input.targetGraphic = img;

        var textGo = new GameObject("Text");
        textGo.transform.SetParent(go.transform, false);
        var textRt = textGo.AddComponent<RectTransform>();
        textRt.anchorMin = Vector2.zero;
        textRt.anchorMax = Vector2.one;
        textRt.offsetMin = new Vector2(20, 0);
        textRt.offsetMax = new Vector2(-20, 0);
        var text = textGo.AddComponent<Text>();
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.fontSize = 40;
        text.color = Color.white;
        text.alignment = TextAnchor.MiddleLeft;
        text.supportRichText = false;
        input.textComponent = text;

        var phGo = new GameObject("Placeholder");
        phGo.transform.SetParent(go.transform, false);
        var phRt = phGo.AddComponent<RectTransform>();
        phRt.anchorMin = Vector2.zero;
        phRt.anchorMax = Vector2.one;
        phRt.offsetMin = new Vector2(20, 0);
        phRt.offsetMax = new Vector2(-20, 0);
        var ph = phGo.AddComponent<Text>();
        ph.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        ph.fontSize = 40;
        ph.color = new Color(0.55f, 0.55f, 0.6f);
        ph.alignment = TextAnchor.MiddleLeft;
        ph.text = placeholder;
        ph.fontStyle = FontStyle.Italic;
        input.placeholder = ph;

        return input;
    }

    Button MakeButton(Transform parent, string label, Vector2 pos, Color color, System.Action onClick)
    {
        var go = new GameObject("Button_" + label);
        go.transform.SetParent(parent, false);
        var rt = go.AddComponent<RectTransform>();
        rt.sizeDelta = new Vector2(500, 130);
        rt.anchoredPosition = pos;
        var img = go.AddComponent<Image>();
        img.color = color;
        img.sprite = uiSprite;
        var btn = go.AddComponent<Button>();
        btn.targetGraphic = img;
        btn.onClick.AddListener(() => onClick());

        var lbl = MakeText(go.transform, label, Vector2.zero, 56);
        lbl.fontStyle = FontStyle.Bold;
        var lblCg = lbl.gameObject.AddComponent<CanvasGroup>();
        lblCg.blocksRaycasts = false;
        lblCg.interactable = false;
        return btn;
    }

    Image MakeBar(Transform parent, Vector2 pos, Color color, float width, float height)
    {
        var bg = new GameObject("BarBg");
        bg.transform.SetParent(parent, false);
        var bgRt = bg.AddComponent<RectTransform>();
        bgRt.sizeDelta = new Vector2(width, height);
        bgRt.anchoredPosition = pos;
        var bgImg = bg.AddComponent<Image>();
        bgImg.color = new Color(0.12f, 0.12f, 0.18f);
        bgImg.sprite = uiSprite;

        var fg = new GameObject("BarFg");
        fg.transform.SetParent(bg.transform, false);
        var fgRt = fg.AddComponent<RectTransform>();
        fgRt.anchorMin = Vector2.zero;
        fgRt.anchorMax = Vector2.one;
        fgRt.offsetMin = new Vector2(2, 2);
        fgRt.offsetMax = new Vector2(-2, -2);
        var fgImg = fg.AddComponent<Image>();
        fgImg.color = color;
        fgImg.sprite = uiSprite;
        fgImg.type = Image.Type.Filled;
        fgImg.fillMethod = Image.FillMethod.Horizontal;
        fgImg.fillOrigin = (int)Image.OriginHorizontal.Left;
        fgImg.fillAmount = 1f;
        return fgImg;
    }
}
