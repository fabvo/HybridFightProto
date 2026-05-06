using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
using UnityEngine.InputSystem.UI;
#endif

/// <summary>
/// Builds the entire UI in code so you don't have to wire anything up in the Unity scene.
///
/// Three top-level panels (only one visible at a time):
///   - Lobby:         Solo / BT Host / BT Verbinden / BT Pairing
///   - Picker:        list of paired BT devices to choose for connecting
///   - Game:          actual gameplay HUD with HP bars, mode, charge, swipe trail
/// </summary>
public class UIManager : MonoBehaviour
{
    GameManager       game;
    NetworkController net;
    BluetoothManager  bt;
    NfcManager        nfc;
    Canvas            canvas;
    RectTransform     canvasRect;

    GameObject lobbyPanel, pickerPanel, gamePanel;
    Text       lobbyStatusText, pickerStatusText, modeText, myHpText, oppHpText;
    Text       gameOverText, hintText, soloBadge, nfcStatusText;
    Image      myHpBar, oppHpBar, chargeBar;
    Image      nfcStatusBg, nfcFlash, attackFlash, swipeLine;
    Text       damagePopup;
    Transform  pickerListContent;

    Sprite uiSprite;
    Color  chargeBarBaseColor   = new Color(1f, 0.85f, 0.20f);
    Color  chargeBarPulseColor  = new Color(1f, 1.0f,  0.55f);

    void Start()
    {
        game = GetComponent<GameManager>();
        net  = GetComponent<NetworkController>();
        bt   = BluetoothManager.Instance;
        nfc  = GameObject.Find("NfcManager").GetComponent<NfcManager>();

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
        if (pickerPanel.activeSelf)
            pickerStatusText.text = bt != null ? bt.Status : "";

        UpdateNfcStatus();
        UpdateSwipeTrail();
        UpdateChargePulse();
    }

    // ===================== Panel switching =====================
    void ShowLobby()
    {
        lobbyPanel.SetActive(true);
        pickerPanel.SetActive(false);
        gamePanel.SetActive(false);
    }

    void ShowPicker()
    {
        lobbyPanel.SetActive(false);
        pickerPanel.SetActive(true);
        gamePanel.SetActive(false);
        RefreshDeviceList();
    }

    void ShowGame()
    {
        lobbyPanel.SetActive(false);
        pickerPanel.SetActive(false);
        gamePanel.SetActive(true);
    }

    void OnConnected()
    {
        oppHpText.text = (net.IsSolo ? "Trainer: " : "Gegner: ") + game.OpponentHp;
        soloBadge.gameObject.SetActive(net.IsSolo);
        ShowGame();
    }

    void OnDisconnected()
    {
        ShowLobby();
    }

    // ===================== NFC status =====================
    void UpdateNfcStatus()
    {
        if (nfc == null || nfcStatusText == null) return;

        if (nfc.LastTagId == null)
        {
            nfcStatusText.text = "NFC: noch kein Tag erkannt";
            nfcStatusBg .color = new Color(0.45f, 0.18f, 0.18f, 1f);
        }
        else if (nfc.TagPresent)
        {
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

    int lastSeenContactCount;
    void OnNfcDiscovered(string uid)
    {
        if (lastSeenContactCount != nfc.TagsDetectedCount)
        {
            lastSeenContactCount = nfc.TagsDetectedCount;
            StartCoroutine(FlashOverlay(nfcFlash, new Color(1f, 0.85f, 0.25f, 0.65f), 0.45f));
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
        BuildPicker(canvasGo.transform);
        BuildGame(canvasGo.transform);
    }

    // ----- Lobby -----
    void BuildLobby(Transform parent)
    {
        lobbyPanel = MakePanel("LobbyPanel", parent, new Color(0.08f, 0.10f, 0.16f));

        var title = MakeText(lobbyPanel.transform, "HYBRID FIGHT", new Vector2(0, 760), 90);
        title.fontStyle = FontStyle.Bold;
        title.color = new Color(0.7f, 1f, 0.7f);

        MakeText(lobbyPanel.transform,
            "Verbinde dich per Bluetooth mit dem Partner-Handy.\n"
          + "Hinweis: Beide Handys muessen einmalig in den Android-Bluetooth-\n"
          + "Einstellungen miteinander gekoppelt sein.",
            new Vector2(0, 540), 28);

        MakeButton(lobbyPanel.transform, "SOLO TEST",     new Vector2(0,  280),
            new Color(0.40f, 0.70f, 0.45f), () => net.StartSolo());

        MakeButton(lobbyPanel.transform, "BT HOST",       new Vector2(0,  100),
            new Color(0.85f, 0.45f, 0.30f), () => net.StartBluetoothHost());

        MakeButton(lobbyPanel.transform, "BT VERBINDEN",  new Vector2(0,  -80),
            new Color(0.30f, 0.55f, 0.85f), ShowPicker);

        MakeButton(lobbyPanel.transform, "PAIRING-EINSTELLUNGEN", new Vector2(0, -240),
            new Color(0.40f, 0.42f, 0.50f), () => bt?.OpenSettings());

        lobbyStatusText = MakeText(lobbyPanel.transform, "Nicht verbunden", new Vector2(0, -550), 30);
        lobbyStatusText.color = new Color(0.7f, 0.7f, 0.8f);
    }

    // ----- Device picker -----
    void BuildPicker(Transform parent)
    {
        pickerPanel = MakePanel("PickerPanel", parent, new Color(0.08f, 0.10f, 0.16f));
        pickerPanel.SetActive(false);

        var title = MakeText(pickerPanel.transform, "GERAET WAEHLEN", new Vector2(0, 800), 70);
        title.fontStyle = FontStyle.Bold;
        title.color = new Color(0.7f, 0.85f, 1f);

        MakeText(pickerPanel.transform,
            "Tippe auf das Partner-Handy um zu verbinden.",
            new Vector2(0, 700), 26);

        // ScrollRect with vertical layout
        var scrollGo = new GameObject("Scroll");
        scrollGo.transform.SetParent(pickerPanel.transform, false);
        var scrollRt = scrollGo.AddComponent<RectTransform>();
        scrollRt.sizeDelta = new Vector2(900, 1100);
        scrollRt.anchoredPosition = new Vector2(0, 50);
        var scrollImg = scrollGo.AddComponent<Image>();
        scrollImg.color = new Color(0.10f, 0.12f, 0.16f, 0.9f);
        scrollImg.sprite = uiSprite;
        scrollGo.AddComponent<RectMask2D>();
        var scrollRect = scrollGo.AddComponent<ScrollRect>();
        scrollRect.horizontal = false;
        scrollRect.vertical   = true;

        var contentGo = new GameObject("Content");
        contentGo.transform.SetParent(scrollGo.transform, false);
        var contentRt = contentGo.AddComponent<RectTransform>();
        contentRt.anchorMin = new Vector2(0, 1);
        contentRt.anchorMax = new Vector2(1, 1);
        contentRt.pivot     = new Vector2(0.5f, 1);
        contentRt.anchoredPosition = Vector2.zero;
        contentRt.sizeDelta = new Vector2(0, 0);

        var vlg = contentGo.AddComponent<VerticalLayoutGroup>();
        vlg.padding             = new RectOffset(20, 20, 20, 20);
        vlg.spacing             = 15;
        vlg.childForceExpandWidth = true;
        vlg.childControlWidth   = true;
        vlg.childForceExpandHeight = false;
        vlg.childControlHeight  = false;
        var fitter = contentGo.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        scrollRect.content  = contentRt;
        scrollRect.viewport = scrollRt;
        pickerListContent   = contentGo.transform;

        MakeButton(pickerPanel.transform, "AKTUALISIEREN", new Vector2(0, -640),
            new Color(0.40f, 0.45f, 0.55f), RefreshDeviceList);
        MakeButton(pickerPanel.transform, "PAIRING-EINSTELLUNGEN", new Vector2(0, -780),
            new Color(0.40f, 0.45f, 0.55f), () => bt?.OpenSettings());
        MakeButton(pickerPanel.transform, "ABBRECHEN", new Vector2(0, -920),
            new Color(0.55f, 0.30f, 0.30f), ShowLobby);

        pickerStatusText = MakeText(pickerPanel.transform, "", new Vector2(0, -1050), 26);
        pickerStatusText.color = new Color(0.7f, 0.7f, 0.8f);
    }

    void RefreshDeviceList()
    {
        if (pickerListContent == null) return;
        for (int i = pickerListContent.childCount - 1; i >= 0; i--)
            Destroy(pickerListContent.GetChild(i).gameObject);

        if (bt == null)
        {
            AddPickerLabel("Bluetooth nicht verfuegbar.");
            return;
        }
        if (!bt.IsSupported())
        {
            AddPickerLabel("Geraet hat kein Bluetooth.");
            return;
        }
        if (!bt.HasConnectPermission())
        {
            AddPickerLabel("Bluetooth-Berechtigung fehlt.");
            AddPickerActionButton("Berechtigung anfragen", () => bt.RequestPermission());
            return;
        }
        if (!bt.IsEnabled())
        {
            AddPickerLabel("Bluetooth ist ausgeschaltet.");
            AddPickerActionButton("Bluetooth einschalten", () => bt.RequestEnable());
            return;
        }

        var devices = bt.GetPairedDevices();
        if (devices.Count == 0)
        {
            AddPickerLabel("Keine gepaarten Geraete gefunden.\n"
                         + "Koppele die Handys zuerst in den\n"
                         + "Bluetooth-Einstellungen.");
            return;
        }

        foreach (var d in devices)
        {
            string capturedAddr = d.Address;
            string capturedName = d.Name;
            AddPickerActionButton(capturedName + "\n" + capturedAddr,
                () => OnDeviceSelected(capturedAddr, capturedName));
        }
    }

    void OnDeviceSelected(string address, string name)
    {
        pickerStatusText.text = "Verbinde mit " + name + "...";
        net.ConnectBluetooth(address);
    }

    void AddPickerLabel(string text)
    {
        var go = new GameObject("Label");
        go.transform.SetParent(pickerListContent, false);
        var rt = go.AddComponent<RectTransform>();
        rt.sizeDelta = new Vector2(0, 200);
        var t = go.AddComponent<Text>();
        t.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        t.fontSize = 32;
        t.alignment = TextAnchor.MiddleCenter;
        t.color = new Color(0.85f, 0.85f, 0.9f);
        t.text = text;
        t.horizontalOverflow = HorizontalWrapMode.Wrap;
        var le = go.AddComponent<LayoutElement>();
        le.preferredHeight = 200;
    }

    void AddPickerActionButton(string label, System.Action onClick)
    {
        var btnGo = new GameObject("Btn_" + label);
        btnGo.transform.SetParent(pickerListContent, false);
        var rt = btnGo.AddComponent<RectTransform>();
        rt.sizeDelta = new Vector2(0, 130);
        var img = btnGo.AddComponent<Image>();
        img.color = new Color(0.30f, 0.55f, 0.85f);
        img.sprite = uiSprite;
        var btn = btnGo.AddComponent<Button>();
        btn.targetGraphic = img;
        btn.onClick.AddListener(() => onClick());
        var le = btnGo.AddComponent<LayoutElement>();
        le.preferredHeight = 130;

        var lbl = MakeText(btnGo.transform, label, Vector2.zero, 32);
        lbl.fontStyle = FontStyle.Bold;
        var cg = lbl.gameObject.AddComponent<CanvasGroup>();
        cg.blocksRaycasts = false;
        cg.interactable = false;
    }

    // ----- Game HUD -----
    void BuildGame(Transform parent)
    {
        gamePanel = MakePanel("GamePanel", parent, new Color(0.05f, 0.06f, 0.10f));
        gamePanel.SetActive(false);

        oppHpText = MakeText(gamePanel.transform, "Gegner: 100", new Vector2(0, 850), 40);
        oppHpBar  = MakeBar (gamePanel.transform, new Vector2(0, 780), new Color(0.9f, 0.3f, 0.3f), 800f, 35f);

        soloBadge = MakeText(gamePanel.transform, "[ SOLO TEST ]", new Vector2(0, 700), 28);
        soloBadge.color = new Color(0.55f, 0.85f, 0.55f);
        soloBadge.gameObject.SetActive(false);

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

        modeText = MakeText(gamePanel.transform, "-", new Vector2(0, 100), 90);
        modeText.fontStyle = FontStyle.Bold;

        hintText = MakeText(gamePanel.transform, "", new Vector2(0, -50), 32);
        hintText.color = new Color(0.7f, 0.7f, 0.8f);

        MakeText(gamePanel.transform, "AUFLADUNG", new Vector2(0, -550), 32);
        chargeBar = MakeBar(gamePanel.transform, new Vector2(0, -620), chargeBarBaseColor, 800f, 35f);

        myHpText = MakeText(gamePanel.transform, "Du: 100", new Vector2(0, -760), 40);
        myHpBar  = MakeBar (gamePanel.transform, new Vector2(0, -830), new Color(0.3f, 0.85f, 0.4f), 800f, 35f);

        gameOverText = MakeText(gamePanel.transform, "", new Vector2(0, 350), 110);
        gameOverText.fontStyle = FontStyle.Bold;
        gameOverText.gameObject.SetActive(false);

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
                hintText.text  = "Halte das Handy aufrecht – du blockst.";
                break;
            case PlayerMode.AttackReady:
                modeText.text  = "ANGRIFF";
                modeText.color = new Color(1f, 0.5f, 0.4f);
                hintText.text  = "Wische ueber den Bildschirm um anzugreifen!";
                break;
            case PlayerMode.Focusing:
                modeText.text  = "FOKUS";
                modeText.color = new Color(1f, 0.9f, 0.3f);
                hintText.text  = "Lade auf... (nicht abheben!)";
                break;
            default:
                modeText.text  = "BEREIT";
                modeText.color = new Color(0.6f, 0.6f, 0.7f);
                hintText.text  = "Aufrecht = Block · Flach hoch = Angriff · Auf NFC-Karte = Fokus";
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

    Button MakeButton(Transform parent, string label, Vector2 pos, Color color, System.Action onClick)
    {
        var go = new GameObject("Button_" + label);
        go.transform.SetParent(parent, false);
        var rt = go.AddComponent<RectTransform>();
        rt.sizeDelta = new Vector2(560, 130);
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
