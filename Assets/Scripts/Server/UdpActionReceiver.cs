using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;

/// <summary>Receives webcam action and shoulder movement from vision_backend over UDP.</summary>
public class UdpActionReceiver : MonoBehaviour
{
    // Main-thread action stream for isolated training; training consumes packets without a combat player.
    public static event Action<string,float,string,float> TutorialAction;

    [Header("UDP")]
    public int port = 5055;

    [Header("Player (found automatically when empty)")]
    public PlayerController playerController;

    [Header("Action confidence")]
    [Range(0f, 1f)] public float minConfidence = 0.7f;

    private UdpClient udpClient;
    private Thread receiveThread;
    private volatile bool isRunning;
    private readonly object packetLock = new object();
    private readonly Queue<string> pendingPackets = new Queue<string>();
    private string lastAction = "NONE";
    private string lastMovement = "NEUTRAL";
    private bool warnedMissingPlayer;
    private float lastPacketTime;
    private bool hasLiveInput;

    [Serializable]
    private class ActionMessage
    {
        public string action;
        public string movement;
        public float confidence;
        public float movement_strength;
        public double timestamp;
    }

    private void OnEnable()
    {
        try
        {
            udpClient = new UdpClient(new IPEndPoint(IPAddress.Loopback, port));
            isRunning = true;
            receiveThread = new Thread(ReceiveData) { IsBackground = true };
            receiveThread.Start();
            Debug.Log("[Vision] UDP receiver listening on 127.0.0.1:" + port);
        }
        catch (Exception e)
        {
            isRunning = false;
            udpClient?.Close();
            udpClient = null;
            Debug.LogError("[Vision] Cannot listen on UDP " + port + ": " + e.Message);
        }
    }

    private void ReceiveData()
    {
        var sender = new IPEndPoint(IPAddress.Any, 0);
        while (isRunning)
        {
            try
            {
                byte[] bytes = udpClient.Receive(ref sender);
                string json = Encoding.UTF8.GetString(bytes);
                lock (packetLock)
                {
                    if (pendingPackets.Count >= 128) pendingPackets.Dequeue();
                    pendingPackets.Enqueue(json);
                }
            }
            catch (SocketException)
            {
                if (isRunning) continue;
                break;
            }
            catch (ObjectDisposedException) { break; }
            catch (Exception) { if (!isRunning) break; }
        }
    }

    private void Update()
    {
        if (hasLiveInput && Time.unscaledTime - lastPacketTime > 1f)
        {
            hasLiveInput = false; lastAction = "NONE";
            if (playerController != null) playerController.ClearExternalInput();
        }
        // Process every message in order, so a short ATTACK followed by IDLE is not lost.
        while (true)
        {
            string json;
            lock (packetLock)
            {
                if (pendingPackets.Count == 0) break;
                json = pendingPackets.Dequeue();
            }
            ProcessPacket(json);
        }
    }

    private void ProcessPacket(string json)
    {
        ActionMessage message;
        try { message = JsonUtility.FromJson<ActionMessage>(json); }
        catch (Exception e)
        {
            Debug.LogWarning("[Vision] Invalid UDP JSON: " + e.Message);
            return;
        }
        if (message == null) return;
        lastPacketTime = Time.unscaledTime; hasLiveInput = true;
        if (TutorialAction != null) {
            string trainingAction=string.IsNullOrEmpty(message.action)?"NONE":message.action.Trim().ToUpperInvariant();
            TutorialAction.Invoke(trainingAction,message.confidence,
                message.movement == null ? "" : message.movement.Trim().ToUpperInvariant(), message.movement_strength);
            return;
        }

        if (playerController == null)
            playerController = FindAnyObjectByType<PlayerController>();
        if (playerController == null)
        {
            if (!warnedMissingPlayer) Debug.LogWarning("[Vision] PlayerController not found in scene.");
            warnedMissingPlayer = true;
            return;
        }

        string movement = string.IsNullOrEmpty(message.movement) ? "NEUTRAL"
            : message.movement.Trim().ToUpperInvariant();
        float axis = movement == "LEFT" ? -1f : movement == "RIGHT" ? 1f : 0f;
        float strength = Mathf.Clamp01(message.movement_strength);
        // Movement is estimated separately from the action classifier, so do not gate it on action confidence.
        playerController.ExternalMovement(axis * strength);
        if (movement != lastMovement)
        {
            Debug.Log("[Vision] Movement: " + movement + " strength " + strength.ToString("F2"));
            lastMovement = movement;
        }

        string action = string.IsNullOrEmpty(message.action) ? "NONE"
            : message.action.Trim().ToUpperInvariant();
        bool passive = action == "NONE" || action == "IDLE";
        // A low-confidence NONE must still release a held block/crouch after tracking is lost.
        if (!passive && message.confidence < minConfidence) return;

        // Backend can resend the same attack when movement changes. Treat attack/jump as edges.
        bool oneShot = action == "ATTACK" || action == "JUMP" || action == "JAB" || action == "STRAIGHT" || action == "CROSS"
            || action == "HIGH_KICK" || action == "HIGHKICK" || action == "LOW_KICK" || action == "LOWKICK"
            || action == "SPECIAL_RELEASE" || action == "SPECIAL";
        if (oneShot && action == lastAction) return;
        if (action != lastAction)
            Debug.Log("[Vision] Action: " + action + " confidence " + message.confidence.ToString("F2"));
        lastAction = action;
        playerController.ExternalAction(action);
    }

    private void OnDisable()
    {
        isRunning = false;
        udpClient?.Close();
        udpClient = null;
        if (receiveThread != null && receiveThread.IsAlive) receiveThread.Join(200);
        receiveThread = null;
        lock (packetLock) pendingPackets.Clear();
        if (playerController != null) playerController.ClearExternalInput();
        lastAction = "NONE";
        lastMovement = "NEUTRAL";
    }
}
