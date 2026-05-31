using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;

public class UdpActionReceiver : MonoBehaviour
{
    [Header("UDP Settings")]
    public int port = 5055;

    [Header("Animator")]
    public Animator animator;

    [Header("Confidence")]
    public float minConfidence = 0.7f;

    private UdpClient udpClient;
    private Thread receiveThread;
    private bool isRunning = false;

    private readonly object lockObject = new object();
    private string latestAction = "";
    private float latestConfidence = 0f;
    private bool hasNewAction = false;

    [Serializable]
    public class ActionMessage
    {
        public string action;
        public float confidence;
        public double timestamp;
    }

    void Start()
    {
        StartReceiver();
    }

    void Update()
    {
        string actionToProcess = "";
        float confidenceToProcess = 0f;

        lock (lockObject)
        {
            if (hasNewAction)
            {
                actionToProcess = latestAction;
                confidenceToProcess = latestConfidence;
                hasNewAction = false;
            }
        }

        if (!string.IsNullOrEmpty(actionToProcess))
        {
            ProcessAction(actionToProcess, confidenceToProcess);
        }
    }

    private void StartReceiver()
    {
        try
        {
            udpClient = new UdpClient(port);
            isRunning = true;

            receiveThread = new Thread(ReceiveData);
            receiveThread.IsBackground = true;
            receiveThread.Start();

            Debug.Log($"UDP Receiver started on port {port}");
        }
        catch (Exception e)
        {
            Debug.LogError($"Failed to start UDP Receiver: {e.Message}");
        }
    }

    private void ReceiveData()
    {
        IPEndPoint remoteEndPoint = new IPEndPoint(IPAddress.Any, port);

        while (isRunning)
        {
            try
            {
                byte[] data = udpClient.Receive(ref remoteEndPoint);
                string json = Encoding.UTF8.GetString(data);

                ActionMessage message = JsonUtility.FromJson<ActionMessage>(json);

                if (message != null)
                {
                    lock (lockObject)
                    {
                        latestAction = message.action;
                        latestConfidence = message.confidence;
                        hasNewAction = true;
                    }
                }
            }
            catch (SocketException)
            {
                // Socket closed during quit.
            }
            catch (Exception e)
            {
                Debug.LogWarning($"UDP receive error: {e.Message}");
            }
        }
    }

    private void ProcessAction(string action, float confidence)
    {
        if (confidence < minConfidence)
        {
            return;
        }

        Debug.Log($"Received Action: {action} / Confidence: {confidence}");

        if (animator == null)
        {
            Debug.LogWarning("Animator is not assigned.");
            return;
        }

        switch (action)
        {
            case "ATTACK":
                animator.SetTrigger("Attack");
                break;

            case "BLOCK":
                animator.SetTrigger("Block");
                break;

            case "JUMP":
                animator.SetTrigger("Jump");
                break;

            case "CROUCH":
                animator.SetTrigger("Crouch");
                break;

            case "IDLE":
                // 필요하면 Idle 트리거 사용
                // animator.SetTrigger("Idle");
                break;

            case "NONE":
                // 아무 동작도 하지 않음
                break;

            default:
                Debug.LogWarning($"Unknown action: {action}");
                break;
        }
    }

    private void OnApplicationQuit()
    {
        isRunning = false;

        if (udpClient != null)
        {
            udpClient.Close();
            udpClient = null;
        }

        if (receiveThread != null && receiveThread.IsAlive)
        {
            receiveThread.Join(100);
            receiveThread = null;
        }
    }
}