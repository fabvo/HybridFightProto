using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
using UnityEngine.InputSystem.UI;
#endif

/// <summary>
/// Full UI built in code. Three panels: Lobby, Search, Game.
///
/// Tag effect display uses TagEffects.DisplayName/Description/Color exclusively,
/// so adding a new effect only requires editing TagEffects.cs.
///
/// Back buttons: Game panel has ZURUECK (disconnects + returns to lobby),
/// Search panel already has ABBRECHEN.
/// </summary>
public class UIManager : MonoBehaviour
{
    GameManager       game;
    NetworkController net;
    NearbyConnectionsManager nearby;
    NfcManager        nfc;
    Canvas            canvas;
    RectTransform     canvasRect;

    GameObject lobbyPanel, searchPanel, gamePanel;
    Text       lobbyStatusText, searchStatusText, modeText, myHpText, oppHpText;
    Text       gameOverText, hintText, soloBadge, nfcStatusText, searchSpinnerText;
    Image      myHpBar, oppHpBar, chargeBar;
    Image      nfcStatusBg, nfcFlash, attackFlash, swipeLine;
    Text       damagePopup;

    Sprite uiSprite;
    Color  chargeBarBaseColor   = new Color(1f, 0.85f, 0.20f);
    Color  chargeBarPulseColor  = new Color(1f, 1.0f,  0.55f);

    void Start()
    {
        game   = GetComponent<GameManager>();
        net    = GetComponent<NetworkController>();
        nearby = NearbyConnectionsManager.Instance;
        nfc    = GameObject.Find("NfcManager").GetComponent<NfcManager>();

        uiSprite = Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f));

        BuildUi();

        net.OnConnected     += OnConnected;
        net.OnDisconnected  += OnDisconnected;
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
        ShowLobby();
    }

    void Update()
    {
        if (lobbyPanel.activeSelf)
            lobbyStatusText.text = net.Status;
        if (searchPanel.activeSelf)
        {
            searchStatusText.text = nearby != null ? nearby.Status : "";
            int dots = (int)((Time.time * 2) % 4);
            searchSpinnerText.text = "Suche Mitspieler" + new string('.', dots);
        }

        UpdateNfcStatus();
        UpdateSwipeTrail();
        UpdateChargePulse();
    }

    // ===================== Panel switching =====================
    void ShowLobby()
    {
        lobbyPanel.SetActive(true);
        searchPanel.SetActive(false);
        gamePanel.SetActive(false);
    }

    void ShowSearch()
    {
        lobbyPanel.SetActive(false);
        searchPanel.SetActive(true);
        gamePanel.SetActive(false);
        net.StartFindingPeer();
    }

    void ShowGame()
    {
        lobbyPanel.SetActive(false);
        searchPanel.SetActive(false);
        gamePanel.SetActive(true);
        gameOverText.gameObject.SetActive(false);
    }

    void OnConnected()
    {
        oppHpText.text = (net.IsSolo ? "Trainer: " : "Gegner: ") + game.OpponentHp;
        soloBadge.gameObject.SetActive(net.IsSolo);
        ShowGame();
    }

    void OnDisconnected() => ShowLobby();

    void CancelSearch()   { net.Disconnect(); ShowLobby(); }

    void LeaveGame()      { net.Disconnect(); }

    // ===================== NFC status (reads TagEffects for display) =====================
    void UpdateNfcStatus()
    {
        if (nfc == null || nfcStatusText == null) return;

        if (nfc.LastTagId == null)
        {
            nfcStatusText.text = "kein Tag";
            nfcStatusBg .color = new Color(0.45f, 0.18f, 0.18f, 1f);
        }
        else if (nfc.TagPresent)
        {
            // Show effect name + color from TagEffects (single source of truth)
            TagEffect fx = nfc.CurrentEffect;
            float pulse = 0.5f + 0.5f * Mathf.Sin(Time.time * 5f);
            nfcStatusText.text = $"{TagEffects.DisplayName(fx)}  UID:{nfc.LastTagId}";
            Color baseCol = TagEffects.Color(fx);
            Color bright  = Color.Lerp(baseCol, Color.white, 0.3f);
            nfcStatusBg.color = Color.Lerp(baseCol * 0.7f, bright, pulse);
        }
        else
        {
            float since = nfc.SecondsSinceLastTag;
            nfcStatusText.text = $"#{nfc.TagsDetectedCount} {TagEffects.DisplayName(nfc.CurrentEffect)}  vor {since:0.0}s";
            nfcStatusBg .color = new Color(0.30f, 0.32f, 0.36f, 1f);
        }
    }

    int lastSeenContactCount;
    void OnNfcDiscovered(string uid)
    {
        if (lastSeenContactCount != nfc.TagsDetectedCount)
        {
            lastSeenContactCount = nfc.TagsDetectedCount;
            Color flashCol = TagEffects.Color(nfc.CurrentEffect);
            flashCol.a = 0.65f;
            StartCoroutine(FlashOverlay(nfcFlash, flashCol, 0.45f));
        }
    }

    void OnNfcError(string err)
    {
        nfcStatusBg.color = new Color(0.55f, 0.10f, 0.10f, 1f);
        nfcStatusText.text = "NFC: " + err;
    }

    // ===================== Swipe trail =====================
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

        Vector2 mid    = (a + b) * 0.5f;
        Vector2 delta  = b - a;
        float   dist   = delta.magnitude;
        float   angle  = Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg;

        var rt = swipeLine.rectTransform;
        rt.anchoredPosition = mid;
        rt.sizeDelta        = new Vector2(dist, 14f);
        rt.localEulerAngles = new Vector3(0, 0, angle);

        float t = Mathf.Clamp01(dist / 600f);
        swipeLine.color = Color.Lerp(new Color(1f, 1f, 1f, 0.55f),
                                     new Color(1f, 0.4f, 0.2f, 0.95f), t);
    }

    void UpdateChargePulse()
    {
        if (chargeBar == null) return;
        if (game != null && game.Mode == PlayerMode.Focusing)
        {
            float pulse = 0.5f + 0.5f * Mathf.Sin(Time.time * 6f);
            chargeBar.color = Color.Lerp(chargeBarBaseColor, chargeBarPulseColor, pulse);
        }
        else chargeBar.color = chargeBarBaseColor;
    }

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
            var c = damagePopup.color; c.a = 1f - p; damagePopup.color = c;
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
            var c = startColor; c.a = startColor.a * (1f - p);
            overlay.color = c;
            yield return null;
        }
        overlay.gameObject.SetActive(false);
    }

    // ===================== Build the UI tree =====================
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

        BuildLobby(canvasGo.transform);
        BuildSearch(canvasGo.transform);
        BuildGame(canvasGo.transform);
    }

    void BuildLobby(Transform parent)
    {
        lobbyPanel = MakePanel("LobbyPanel", parent, new Color(0.08f, 0.10f, 0.16f));

        var title = MakeText(lobbyPanel.transform, "HYBRID FIGHT", new Vector2(0, 700), 90);
        title.fontStyle = FontStyle.Bold;
        title.color = new Color(0.7f, 1f, 0.7f);

        MakeText(lobbyPanel.transform,
            "Beide Spieler druecken einfach\n"
          + "MITSPIELER SUCHEN.\n"
          + "Die Handys finden sich automatisch.",
            new Vector2(0, 440), 30);

        MakeText(lobbyPanel.transform,
            "NFC-Karten mit Effekten beschreiben:\n"
          + "NFC Tools PRO -> Schreiben -> Text ->\n"
          + "FOCUS / HEAL / SHIELD / BOMB",
            new Vector2(0, 230), 26);

        MakeButton(lobbyPanel.transform, "MITSPIELER SUCHEN", new Vector2(0, 30),
            new Color(0.30f, 0.55f, 0.85f), ShowSearch);

        MakeButton(lobbyPanel.transform, "SOLO TEST", new Vector2(0, -150),
            new Color(0.40f, 0.70f, 0.45f), () => net.StartSolo());

        lobbyStatusText = MakeText(lobbyPanel.transform, "Nicht verbunden", new Vector2(0, -550), 30);
        lobbyStatusText.color = new Color(0.7f, 0.7f, 0.8f);
    }

    void BuildSearch(Transform parent)
    {
        searchPanel = MakePanel("SearchPanel", parent, new Color(0.08f, 0.10f, 0.16f));
        searchPanel.SetActive(false);

        searchSpinnerText = MakeText(searchPanel.transform, "Suche Mitspieler...", new Vector2(0, 250), 60);
        searchSpinnerText.fontStyle = FontStyle.Bold;
        searchSpinnerText.color = new Color(0.7f, 0.85f, 1f);

        MakeText(searchPanel.transform,
            "Stelle sicher, dass das andere Handy ebenfalls\n"
          + "MITSPIELER SUCHEN gedrueckt hat.\n\n"
          + "Bluetooth und WLAN sollten an sein.",
            new Vector2(0, 0), 28);

        searchStatusText = MakeText(searchPanel.transform, "", new Vector2(0, -250), 28);
        searchStatusText.color = new Color(0.85f, 0.85f, 0.6f);

        MakeButton(searchPanel.transform, "ZURUECK", new Vector2(0, -550),
            new Color(0.55f, 0.30f, 0.30f), CancelSearch);
    }

    void BuildGame(Transform parent)
    {
        gamePanel = MakePanel("GamePanel", parent, new Color(0.05f, 0.06f, 0.10f));
        gamePanel.SetActive(false);

        oppHpText = MakeText(gamePanel.transform, "Gegner: 100", new Vector2(0, 850), 40);
        oppHpBar  = MakeBar (gamePanel.transform, new Vector2(0, 780), new Color(0.9f, 0.3f, 0.3f), 800f, 35f);

        soloBadge = MakeText(gamePanel.transform, "[ SOLO TEST ]", new Vector2(0, 710), 28);
        soloBadge.color = new Color(0.55f, 0.85f, 0.55f);
        soloBadge.gameObject.SetActive(false);

        // NFC status badge
        var nfcGo = new GameObject("NfcStatus");
        nfcGo.transform.SetParent(gamePanel.transform, false);
        var nfcRt = nfcGo.AddComponent<RectTransform>();
        nfcRt.sizeDelta = new Vector2(960, 70);
        nfcRt.anchoredPosition = new Vector2(0, 610);
        nfcStatusBg = nfcGo.AddComponent<Image>();
        nfcStatusBg.sprite = uiSprite;
        nfcStatusBg.color = new Color(0.45f, 0.18f, 0.18f, 1f);
        nfcStatusText = MakeText(nfcGo.transform, "kein Tag", Vector2.zero, 28);
        nfcStatusText.color = new Color(1f, 1f, 1f, 0.95f);

        modeText = MakeText(gamePanel.transform, "-", new Vector2(0, 200), 90);
        modeText.fontStyle = FontStyle.Bold;

        hintText = MakeText(gamePanel.transform, "", new Vector2(0, 50), 30);
        hintText.color = new Color(0.7f, 0.7f, 0.8f);

        MakeText(gamePanel.transform, "AUFLADUNG", new Vector2(0, -450), 32);
        chargeBar = MakeBar(gamePanel.transform, new Vector2(0, -510), chargeBarBaseColor, 800f, 35f);

        myHpText = MakeText(gamePanel.transform, "Du: 100", new Vector2(0, -640), 40);
        myHpBar  = MakeBar (gamePanel.transform, new Vector2(0, -710), new Color(0.3f, 0.85f, 0.4f), 800f, 35f);

        // Back button
        MakeButton(gamePanel.transform, "ZURUECK", new Vector2(0, -880),
            new Color(0.45f, 0.30f, 0.30f), LeaveGame);

        gameOverText = MakeText(gamePanel.transform, "", new Vector2(0, 420), 110);
        gameOverText.fontStyle = FontStyle.Bold;
        gameOverText.gameObject.SetActive(false);

        // Overlays
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

        damagePopup = MakeText(gamePanel.transform, "", new Vector2(0, 200), 84);
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
        img.color  = color;
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
                modeText.text  = "BLOCK";
                modeText.color = new Color(0.4f, 0.7f, 1f);
                hintText.text  = "Halte das Handy aufrecht - du blockst.";
                break;
            case PlayerMode.AttackReady:
                modeText.text  = "ANGRIFF";
                modeText.color = new Color(1f, 0.5f, 0.4f);
                hintText.text  = "Wische ueber den Bildschirm um anzugreifen!";
                break;
            case PlayerMode.Focusing:
                // Display effect name + description from TagEffects (single source of truth)
                TagEffect fx = nfc != null && nfc.TagPresent ? nfc.CurrentEffect : TagEffect.Focus;
                modeText.text  = TagEffects.DisplayName(fx);
                modeText.color = TagEffects.Color(fx);
                hintText.text  = TagEffects.Description(fx);
                break;
            default:
                modeText.text  = "BEREIT";
                modeText.color = new Color(0.6f, 0.6f, 0.7f);
                hintText.text  = "Aufrecht = Block  |  Flach = Angriff  |  NFC-Karte = Effekt";
                break;
        }
    }

    void ShowGameOver()
    {
        bool won = game.MyHp > 0;
        gameOverText.text  = won ? "GEWONNEN!" : "VERLOREN";
        gameOverText.color = won ? new Color(0.4f, 1f, 0.5f) : new Color(1f, 0.3f, 0.3f);
        gameOverText.gameObject.SetActive(true);
    }

    // ===== UGUI build helpers ==============================================
    GameObject MakePanel(string name, Transform parent, Color bg)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
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
        rt.sizeDelta = new Vector2(1000, 200);
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

    Button MakeButton(Transform parent, string label, Vector2 pos, Color color, System.Action onClick)
    {
        var go = new GameObject("Button_" + label);
        go.transform.SetParent(parent, false);
        var rt = go.AddComponent<RectTransform>();
        rt.sizeDelta = new Vector2(720, 130);
        rt.anchoredPosition = pos;
        var img = go.AddComponent<Image>();
        img.color = color;
        img.sprite = uiSprite;
        var btn = go.AddComponent<Button>();
        btn.targetGraphic = img;
        btn.onClick.AddListener(() => onClick());

        var lbl = MakeText(go.transform, label, Vector2.zero, 48);
        lbl.fontStyle = FontStyle.Bold;
        var cg = lbl.gameObject.AddComponent<CanvasGroup>();
        cg.blocksRaycasts = false;
        cg.interactable = false;
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
        fgRt.anchorMin = Vector2.zero; fgRt.anchorMax = Vector2.one;
        fgRt.offsetMin = new Vector2(2, 2); fgRt.offsetMax = new Vector2(-2, -2);
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
