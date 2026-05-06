using System;
using System.Collections;
using UnityEngine;

/// <summary>
/// Network + Solo orchestrator. Replaces the previous TCP-over-WLAN transport
/// with Bluetooth Classic / RFCOMM. WLAN no longer needed -- works in guest
/// networks, no IP entry, no shared SSID.
///
/// Modes:
///   Disconnected: nothing running.
///   Solo:         no transport, an internal AI trainer plays the opponent.
///   Bluetooth:    BluetoothManager (which talks to the Java bridge) handles
///                 the byte-level transport. We just send/receive lines.
///
/// The line-based message format (ATK:N, HP:N, MODE:X) is the same as before
/// so GameManager doesn't change.
/// </summary>
public class NetworkController : MonoBehaviour
{
    public bool   IsConnected { get; private set; }
    public bool   IsSolo      { get; private set; }
    public string Status      { get; private set; } = "Nicht verbunden";

    public event Action      OnConnected;
    public event Action      OnDisconnected;
    public event Action<int> OnAttackReceived;
    public event Action<int> OnOpponentHpUpdated;
    public event Action<PlayerMode> OnOpponentModeChanged;

    BluetoothManager bt;
    GameManager      game;

    // Solo state
    int   soloAiHp;
    float soloNextAiAttack;
    bool  soloAiDead;

    void Start()
    {
        game = GetComponent<GameManager>();
        bt   = BluetoothManager.Instance;

        if (bt != null)
        {
            bt.OnConnected     += OnBluetoothConnected;
            bt.OnDisconnected  += OnBluetoothDisconnected;
            bt.OnMessage       += OnBluetoothMessage;
            bt.OnStatusChanged += s => { if (!IsSolo) Status = s; };
        }
    }

    void Update()
    {
        if (IsSolo && IsConnected) UpdateSoloAi();
    }

    // ===================== Mode entry points =====================
    public void StartSolo()
    {
        if (IsConnected) return;
        IsSolo      = true;
        IsConnected = true;
        Status      = "Solo-Test - KI Trainer";
        soloAiHp         = GameManager.MaxHp;
        soloAiDead       = false;
        soloNextAiAttack = Time.time + 4f;
        OnConnected?.Invoke();
        OnOpponentHpUpdated?.Invoke(soloAiHp);
    }

    public void StartBluetoothHost()
    {
        if (IsConnected || bt == null) return;
        bt.StartHost();
    }

    public void ConnectBluetooth(string deviceAddress)
    {
        if (IsConnected || bt == null) return;
        bt.Connect(deviceAddress);
    }

    public void Disconnect()
    {
        if (IsSolo)
        {
            IsSolo = false;
            IsConnected = false;
            Status = "Nicht verbunden";
            OnDisconnected?.Invoke();
            return;
        }
        bt?.Disconnect();
    }

    // ===================== Bluetooth callbacks =====================
    void OnBluetoothConnected()
    {
        IsSolo      = false;
        IsConnected = true;
        Status      = "Verbunden!";
        OnConnected?.Invoke();
    }

    void OnBluetoothDisconnected()
    {
        IsConnected = false;
        Status      = "Verbindung getrennt";
        OnDisconnected?.Invoke();
    }

    void OnBluetoothMessage(string line)
    {
        var parts = line.Split(':');
        switch (parts[0])
        {
            case "ATK":
                if (parts.Length > 1 && int.TryParse(parts[1], out var dmg))
                    OnAttackReceived?.Invoke(dmg);
                break;
            case "HP":
                if (parts.Length > 1 && int.TryParse(parts[1], out var hp))
                    OnOpponentHpUpdated?.Invoke(hp);
                break;
            case "MODE":
                if (parts.Length > 1 && Enum.TryParse<PlayerMode>(parts[1], out var m))
                    OnOpponentModeChanged?.Invoke(m);
                break;
        }
    }

    // ===================== Solo AI trainer =====================
    void UpdateSoloAi()
    {
        if (soloAiDead) return;
        if (game != null && game.MyHp <= 0) return;
        if (Time.time < soloNextAiAttack) return;

        int dmg = UnityEngine.Random.Range(8, 18);
        OnAttackReceived?.Invoke(dmg);
        soloNextAiAttack = Time.time + UnityEngine.Random.Range(3f, 6f);
    }

    IEnumerator SoloRespawnAi()
    {
        yield return new WaitForSeconds(2f);
        soloAiHp     = GameManager.MaxHp;
        soloAiDead   = false;
        soloNextAiAttack = Time.time + 3f;
        OnOpponentHpUpdated?.Invoke(soloAiHp);
    }

    // ===================== Outbound messages (used by GameManager) =====================
    public void SendAttack(int damage)
    {
        if (IsSolo)
        {
            soloAiHp = Mathf.Max(0, soloAiHp - damage);
            OnOpponentHpUpdated?.Invoke(soloAiHp);
            if (soloAiHp <= 0 && !soloAiDead)
            {
                soloAiDead = true;
                StartCoroutine(SoloRespawnAi());
            }
            return;
        }
        bt?.Send("ATK:" + damage);
    }

    public void SendHp(int hp)
    {
        if (IsSolo) return;
        bt?.Send("HP:" + hp);
    }

    public void SendMode(PlayerMode m)
    {
        if (IsSolo) return;
        bt?.Send("MODE:" + m);
    }
}
