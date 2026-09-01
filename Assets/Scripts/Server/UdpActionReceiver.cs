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

    [Header("Player")]
    [Tooltip("비워두면 씬에서 자동으로 찾는다.")]
    public PlayerController playerController;

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

        // 1순위: PlayerController 를 통해 처리한다.
        // 애니메이터만 건드리면 그림만 바뀌고 데미지 판정이 안 들어가므로,
        // 반드시 PlayerController.ExternalAction() 을 거쳐야 한다.
        if (playerController == null)
            playerController = FindAnyObjectByType<PlayerController>();

        if (playerController != null)
        {
            playerController.externalControl = true;
            playerController.ExternalAction(action);
            return;
        }

        // 2순위(폴백): PlayerController 가 없으면 애니메이터만 재생한다.
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
                // PlayerAnimator 에는 Block 트리거가 없고 IsBlocking bool 만 있다.
                animator.SetBool("IsBlocking", true);
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