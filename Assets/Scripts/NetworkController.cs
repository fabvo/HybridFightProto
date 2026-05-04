using System;
using System.Collections;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using UnityEngine;

/// <summary>
/// Network + Solo transport.
///
/// Multiplayer mode: line-based TCP between two phones over local WiFi.
/// One device hosts (StartHost), the other joins (Join(ip)).
///
/// Solo mode (StartSolo): no networking, a local AI trainer plays the role of the
/// opponent. The AI fires periodic attacks via OnAttackReceived and tracks its own
/// HP through OnOpponentHpUpdated, so GameManager and UIManager don't need to know
/// the difference. Both sides respawn automatically so you can keep testing tilt,
/// NFC, blocking, focus and swipe-attack endlessly.
/// </summary>
public class NetworkController : MonoBehaviour
{
    public const int Port = 7788;

    public bool   IsConnected { get; private set; }
    public bool   IsHost      { get; private set; }
    public bool   IsSolo      { get; private set; }
    public string Status      { get; private set; } = "Nicht verbunden";
    public string LocalIp     => GetLocalIPv4();

    public event Action      OnConnected;
    public event Action      OnDisconnected;
    public event Action<int> OnAttackReceived;
    public event Action<int> OnOpponentHpUpdated;
    public event Action<PlayerMode> OnOpponentModeChanged;

    // -- multiplayer state --
    TcpListener  listener;
    TcpClient    client;
    StreamReader reader;
    StreamWriter writer;
    Thread       receiveThread;
    Thread       acceptThread;

    // Background threads enqueue work onto this; Update() drains it on the main thread.
    readonly ConcurrentQueue<Action> mainThread = new ConcurrentQueue<Action>();

    // -- solo state --
    GameManager game;
    int   soloAiHp;
    float soloNextAiAttack;
    bool  soloAiDead;

    void Start()
    {
        // Cached only for solo mode; we use it to pause AI attacks while the
        // human player is in their respawn timeout.
        game = GetComponent<GameManager>();
    }

    void Update()
    {
        while (mainThread.TryDequeue(out var a)) a();

        if (IsSolo && IsConnected) UpdateSoloAi();
    }

    // =======================================================================
    // Multiplayer host / join
    // =======================================================================
    public void StartHost()
    {
        if (IsConnected) return;
        IsHost = true;
        Status = $"Hosting auf {LocalIp}:{Port} – warte auf Gegner...";
        try
        {
            listener = new TcpListener(IPAddress.Any, Port);
            listener.Start();
        }
        catch (Exception e)
        {
            Status = "Host fehlgeschlagen: " + e.Message;
            Debug.LogError(e);
            return;
        }

        acceptThread = new Thread(() =>
        {
            try
            {
                client = listener.AcceptTcpClient();
                Setup(client);
            }
            catch (Exception e) { Debug.LogError(e); }
        }) { IsBackground = true };
        acceptThread.Start();
    }

    public void Join(string ip)
    {
        if (IsConnected) return;
        IsHost = false;
        Status = $"Verbinde mit {ip}...";
        try
        {
            client = new TcpClient();
            client.Connect(ip.Trim(), Port);
            Setup(client);
        }
        catch (Exception e)
        {
            Status = "Verbindung fehlgeschlagen: " + e.Message;
            Debug.LogError(e);
        }
    }

    void Setup(TcpClient c)
    {
        var stream = c.GetStream();
        reader = new StreamReader(stream);
        writer = new StreamWriter(stream) { AutoFlush = true };

        mainThread.Enqueue(() =>
        {
            IsConnected = true;
            Status = "Verbunden!";
            OnConnected?.Invoke();
        });

        receiveThread = new Thread(ReceiveLoop) { IsBackground = true };
        receiveThread.Start();
    }

    void ReceiveLoop()
    {
        try
        {
            string line;
            while ((line = reader.ReadLine()) != null) Handle(line);
        }
        catch (Exception e) { Debug.Log("Receive ended: " + e.Message); }

        mainThread.Enqueue(() =>
        {
            IsConnected = false;
            Status = "Verbindung getrennt.";
            OnDisconnected?.Invoke();
        });
    }

    void Handle(string line)
    {
        var parts = line.Split(':');
        switch (parts[0])
        {
            case "ATK":
                if (parts.Length > 1 && int.TryParse(parts[1], out var dmg))
                    mainThread.Enqueue(() => OnAttackReceived?.Invoke(dmg));
                break;
            case "HP":
                if (parts.Length > 1 && int.TryParse(parts[1], out var hp))
                    mainThread.Enqueue(() => OnOpponentHpUpdated?.Invoke(hp));
                break;
            case "MODE":
                if (parts.Length > 1 && Enum.TryParse<PlayerMode>(parts[1], out var m))
                    mainThread.Enqueue(() => OnOpponentModeChanged?.Invoke(m));
                break;
        }
    }

    void Send(string msg)
    {
        if (!IsConnected || writer == null) return;
        try { writer.WriteLine(msg); }
        catch (Exception e) { Debug.LogError(e); }
    }

    // =======================================================================
    // Solo mode -- local AI trainer for testing without a second phone
    // =======================================================================
    public void StartSolo()
    {
        if (IsConnected) return;
        IsSolo      = true;
        IsConnected = true;
        IsHost      = true;
        Status      = "Solo-Test – KI Trainer";
        soloAiHp         = GameManager.MaxHp;
        soloAiDead       = false;
        soloNextAiAttack = Time.time + 4f;
        OnConnected?.Invoke();
        OnOpponentHpUpdated?.Invoke(soloAiHp);
    }

    void UpdateSoloAi()
    {
        if (soloAiDead) return;
        if (game != null && game.MyHp <= 0) return; // player is in respawn timeout
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

    // =======================================================================
    // Public API used by GameManager
    // =======================================================================
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
        Send("ATK:" + damage);
    }

    public void SendHp(int hp)
    {
        if (IsSolo) return;
        Send("HP:" + hp);
    }

    public void SendMode(PlayerMode m)
    {
        if (IsSolo) return;
        Send("MODE:" + m);
    }

    void OnDestroy()
    {
        try { client?.Close(); }   catch { }
        try { listener?.Stop(); }  catch { }
    }

    static string GetLocalIPv4()
    {
        try
        {
            foreach (var ni in System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != System.Net.NetworkInformation.OperationalStatus.Up) continue;
                if (ni.NetworkInterfaceType == System.Net.NetworkInformation.NetworkInterfaceType.Loopback) continue;
                foreach (var ip in ni.GetIPProperties().UnicastAddresses)
                {
                    if (ip.Address.AddressFamily == AddressFamily.InterNetwork &&
                        !IPAddress.IsLoopback(ip.Address))
                    {
                        return ip.Address.ToString();
                    }
                }
            }
        }
        catch { }
        return "?";
    }
}
