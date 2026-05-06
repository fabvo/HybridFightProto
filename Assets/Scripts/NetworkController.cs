using System;
using System.Collections;
using UnityEngine;

/// <summary>
/// Network + Solo orchestrator. Uses Google Nearby Connections instead of BT pairing
/// or TCP-over-WLAN. Players just press one button on each phone and the API does
/// the rest -- no IP, no pairing, no shared WiFi.
///
/// The line-based message format (ATK:N, HP:N, MODE:X) is unchanged so GameManager
/// stays the same.
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

    NearbyConnectionsManager nearby;
    GameManager              game;

    // Solo state
    int   soloAiHp;
    float soloNextAiAttack;
    bool  soloAiDead;

    void Start()
    {
        game   = GetComponent<GameManager>();
        nearby = NearbyConnectionsManager.Instance;

        if (nearby != null)
        {
            nearby.OnConnected     += OnNearbyConnected;
            nearby.OnDisconnected  += OnNearbyDisconnected;
            nearby.OnMessage       += OnNearbyMessage;
            nearby.OnStatusChanged += s => { if (!IsSolo) Status = s; };
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

    public void StartFindingPeer()
    {
        if (IsConnected || nearby == null) return;
        nearby.StartFindingPeer();
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
        nearby?.Stop();
    }

    // ===================== Nearby callbacks =====================
    void OnNearbyConnected()
    {
        IsSolo      = false;
        IsConnected = true;
        Status      = "Verbunden!";
        OnConnected?.Invoke();
    }

    void OnNearbyDisconnected()
    {
        IsConnected = false;
        Status      = "Verbindung getrennt";
        OnDisconnected?.Invoke();
    }

    void OnNearbyMessage(string line)
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

    // ===================== Outbound (used by GameManager) =====================
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
        nearby?.Send("ATK:" + damage);
    }

    public void SendHp(int hp)
    {
        if (IsSolo) return;
        nearby?.Send("HP:" + hp);
    }

    public void SendMode(PlayerMode m)
    {
        if (IsSolo) return;
        nearby?.Send("MODE:" + m);
    }
}
