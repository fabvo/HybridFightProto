package com.example.hybridfight;

import android.app.Activity;
import android.bluetooth.BluetoothAdapter;
import android.bluetooth.BluetoothDevice;
import android.bluetooth.BluetoothServerSocket;
import android.bluetooth.BluetoothSocket;
import android.content.Intent;
import android.content.pm.PackageManager;
import android.os.Build;
import android.provider.Settings;
import android.util.Log;

import com.unity3d.player.UnityPlayer;

import java.io.BufferedReader;
import java.io.IOException;
import java.io.InputStreamReader;
import java.io.OutputStream;
import java.io.OutputStreamWriter;
import java.io.PrintWriter;
import java.nio.charset.StandardCharsets;
import java.util.Set;
import java.util.UUID;

/**
 * Bluetooth Classic / RFCOMM bridge for the Unity game.
 *
 * Design choices:
 *   - "Insecure RFCOMM" so we don't trigger a PIN dialog. Devices must already
 *     be paired in Android Bluetooth settings (one-time setup), but no extra
 *     PIN is needed when the app connects.
 *   - Paired-devices-only flow: we never run BluetoothAdapter.startDiscovery().
 *     This avoids the complicated BLUETOOTH_SCAN runtime permission and the
 *     ACCESS_FINE_LOCATION quirk on older Android. Pairing in system settings
 *     is the canonical UX for "I want these two phones to know each other".
 *   - All blocking I/O on background threads. Reads forward each line via
 *     UnityPlayer.UnitySendMessage("BluetoothManager", "OnBtMessage", line).
 *     Status changes (connected, disconnected, errors) go through OnBtStatus.
 *
 * The class is fully static -- it is invoked from Unity via AndroidJavaClass
 * with no instance to keep around.
 */
public class BluetoothBridge {

    private static final String TAG = "BluetoothBridge";

    /** Service name shown to other devices via SDP. */
    private static final String SDP_NAME = "HybridFight";

    /**
     * Custom UUID generated specifically for this app. Both phones look for
     * this UUID, ensuring we only connect to other instances of the game and
     * not to random services on the paired device.
     * 8-4-4-4-12 hex digits, RFC 4122 format.
     */
    private static final UUID SERVICE_UUID =
            UUID.fromString("a7d2c1b0-3e4f-4a51-9b6c-2025fe11ce11");

    private static final int REQUEST_CODE_PERMISSIONS = 8772;

    private static volatile BluetoothAdapter adapter;
    private static volatile BluetoothServerSocket serverSocket;
    private static volatile BluetoothSocket socket;
    private static volatile PrintWriter writer;
    private static volatile Thread acceptThread;
    private static volatile Thread readThread;

    // ---------- Adapter access ----------

    private static Activity activity() {
        return UnityPlayer.currentActivity;
    }

    private static BluetoothAdapter ensureAdapter() {
        if (adapter == null) adapter = BluetoothAdapter.getDefaultAdapter();
        return adapter;
    }

    // ---------- Status / capability queries (called from Unity) ----------

    public static boolean isSupported() {
        return ensureAdapter() != null;
    }

    public static boolean isEnabled() {
        BluetoothAdapter a = ensureAdapter();
        return a != null && a.isEnabled();
    }

    public static boolean hasConnectPermission() {
        if (Build.VERSION.SDK_INT < 31) return true;
        return activity().checkSelfPermission("android.permission.BLUETOOTH_CONNECT")
                == PackageManager.PERMISSION_GRANTED;
    }

    public static void requestConnectPermission() {
        if (Build.VERSION.SDK_INT < 31) return;
        try {
            activity().requestPermissions(
                    new String[]{"android.permission.BLUETOOTH_CONNECT"},
                    REQUEST_CODE_PERMISSIONS);
        } catch (Throwable t) {
            Log.w(TAG, "requestPermissions failed: " + t);
        }
    }

    public static void requestEnable() {
        if (!hasConnectPermission()) {
            requestConnectPermission();
            return;
        }
        try {
            Intent i = new Intent(BluetoothAdapter.ACTION_REQUEST_ENABLE);
            activity().startActivity(i);
        } catch (SecurityException se) {
            sendStatus("ERROR Berechtigung fehlt");
        } catch (Throwable t) {
            sendStatus("ERROR " + t.getMessage());
        }
    }

    /** Opens the system Bluetooth settings page so the user can pair devices. */
    public static void openSettings() {
        try {
            Intent i = new Intent(Settings.ACTION_BLUETOOTH_SETTINGS);
            activity().startActivity(i);
        } catch (Throwable t) {
            Log.w(TAG, "openSettings failed: " + t);
        }
    }

    /**
     * Returns paired devices joined as one big string, one device per line,
     * format: "<DisplayName>|<MacAddress>".
     * Returns "" if Bluetooth is unavailable or the connect permission is missing.
     * Wrapped in a string because AndroidJavaClass.CallStatic doesn't support
     * String[] return values cleanly across all Unity versions.
     */
    public static String getPairedDevicesPipeSeparated() {
        BluetoothAdapter a = ensureAdapter();
        if (a == null || !hasConnectPermission()) return "";
        try {
            Set<BluetoothDevice> bonded = a.getBondedDevices();
            StringBuilder sb = new StringBuilder();
            for (BluetoothDevice d : bonded) {
                String name;
                try { name = d.getName(); } catch (SecurityException se) { name = "?"; }
                if (name == null) name = "?";
                if (sb.length() > 0) sb.append("\n");
                sb.append(name).append("|").append(d.getAddress());
            }
            return sb.toString();
        } catch (SecurityException se) {
            sendStatus("ERROR Berechtigung verweigert");
            return "";
        }
    }

    // ---------- Host / client lifecycle ----------

    public static void startHost() {
        if (!precheck()) return;
        stop(); // ensure clean state
        sendStatus("Hosting (Bluetooth) - warte auf Verbindung...");
        Thread t = new Thread(BluetoothBridge::runAccept, "BtAccept");
        acceptThread = t;
        t.start();
    }

    private static void runAccept() {
        try {
            BluetoothServerSocket ss = adapter.listenUsingInsecureRfcommWithServiceRecord(
                    SDP_NAME, SERVICE_UUID);
            serverSocket = ss;
            BluetoothSocket s = ss.accept();              // blocks until partner connects
            try { ss.close(); } catch (IOException ignored) {}
            serverSocket = null;
            attach(s);
        } catch (IOException e) {
            sendStatus("Host-Fehler: " + e.getMessage());
            cleanup();
        } catch (SecurityException se) {
            sendStatus("ERROR Berechtigung fehlt");
            cleanup();
        }
    }

    public static void connectTo(String address) {
        if (!precheck()) return;
        if (address == null || address.isEmpty()) {
            sendStatus("ERROR Keine Geraete-Adresse");
            return;
        }
        stop();
        sendStatus("Verbinde mit " + address + "...");
        final String addr = address;
        Thread t = new Thread(() -> runConnect(addr), "BtConnect");
        t.start();
    }

    private static void runConnect(String address) {
        try {
            BluetoothDevice device = adapter.getRemoteDevice(address);
            // Discovery massively slows down RFCOMM connect on most phones.
            try { adapter.cancelDiscovery(); } catch (SecurityException ignored) {}
            BluetoothSocket s = device.createInsecureRfcommSocketToServiceRecord(SERVICE_UUID);
            s.connect();                                  // blocks until accept or fail
            attach(s);
        } catch (IOException e) {
            sendStatus("Verbindung fehlgeschlagen: " + e.getMessage());
            cleanup();
        } catch (SecurityException se) {
            sendStatus("ERROR Berechtigung fehlt");
            cleanup();
        }
    }

    private static void attach(BluetoothSocket s) {
        socket = s;
        try {
            OutputStream out = s.getOutputStream();
            writer = new PrintWriter(new OutputStreamWriter(out, StandardCharsets.UTF_8), true);
            BufferedReader reader = new BufferedReader(
                    new InputStreamReader(s.getInputStream(), StandardCharsets.UTF_8));

            sendStatus("CONNECTED");
            Thread r = new Thread(() -> runRead(reader), "BtRead");
            readThread = r;
            r.start();
        } catch (IOException e) {
            sendStatus("Stream-Fehler: " + e.getMessage());
            cleanup();
        }
    }

    private static void runRead(BufferedReader reader) {
        try {
            String line;
            while ((line = reader.readLine()) != null) {
                UnityPlayer.UnitySendMessage("BluetoothManager", "OnBtMessage", line);
            }
        } catch (IOException e) {
            // Connection closed by partner or transport error -- handled below.
        }
        sendStatus("DISCONNECTED");
        cleanup();
    }

    public static void send(String message) {
        PrintWriter w = writer;
        if (w == null) return;
        try {
            w.println(message);
        } catch (Exception e) {
            Log.w(TAG, "send failed: " + e);
        }
    }

    public static void stop() {
        cleanup();
    }

    private static void cleanup() {
        try { if (writer != null) writer.close(); } catch (Throwable ignored) {}
        writer = null;
        try { if (socket != null) socket.close(); } catch (Throwable ignored) {}
        socket = null;
        try { if (serverSocket != null) serverSocket.close(); } catch (Throwable ignored) {}
        serverSocket = null;

        Thread a = acceptThread; acceptThread = null;
        if (a != null && a != Thread.currentThread()) a.interrupt();
        Thread r = readThread; readThread = null;
        if (r != null && r != Thread.currentThread()) r.interrupt();
    }

    private static boolean precheck() {
        BluetoothAdapter a = ensureAdapter();
        if (a == null) {
            sendStatus("ERROR Kein Bluetooth verfuegbar");
            return false;
        }
        if (!hasConnectPermission()) {
            sendStatus("ERROR Bluetooth-Berechtigung fehlt");
            requestConnectPermission();
            return false;
        }
        if (!a.isEnabled()) {
            sendStatus("ERROR Bluetooth ist aus");
            return false;
        }
        return true;
    }

    private static void sendStatus(String s) {
        Log.d(TAG, "Status -> " + s);
        try {
            UnityPlayer.UnitySendMessage("BluetoothManager", "OnBtStatus", s);
        } catch (Throwable ignored) {}
    }
}
