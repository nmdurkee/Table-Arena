using System;
using System.Runtime.InteropServices;
using UnityEngine;

public class SignalHandler : MonoBehaviour
{
    #region Native Imports
#if UNITY_STANDALONE_LINUX || UNITY_EDITOR_LINUX
    [DllImport("SignalHandler")]
    private static extern int InitializeSignalHandling();

    [DllImport("SignalHandler")]
    private static extern void RegisterSignalCallback(IntPtr callback);

    [DllImport("SignalHandler")]
    private static extern IntPtr GetSignalName(int sig);
#endif
    #endregion

    private delegate void SignalCallbackDelegate(int signalNumber);
    private static SignalCallbackDelegate s_callbackDelegate = null;
    private static SignalHandler s_instance;

    private void Awake()
    {
        if (s_instance != null && s_instance != this)
        {
            Destroy(gameObject);
            return;
        }
        s_instance = this;
        DontDestroyOnLoad(gameObject);
    }

    public static void Initialize()
    {
#if UNITY_STANDALONE_LINUX || UNITY_EDITOR_LINUX
        InitializeLinuxSignalHandling();
#elif UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        InitializeWindowsSignalHandling();
#else
        Debug.Log("[SignalHandler] Signal handling not implemented for this platform.");
#endif
    }

    private static void InitializeLinuxSignalHandling()
    {
#if UNITY_STANDALONE_LINUX || UNITY_EDITOR_LINUX
        try
        {
            s_callbackDelegate = new SignalCallbackDelegate(OnSignalReceived);
            IntPtr callbackPtr = Marshal.GetFunctionPointerForDelegate(s_callbackDelegate);
            RegisterSignalCallback(callbackPtr);
            int result = InitializeSignalHandling();
            if (result == 0)
                Debug.Log("[SignalHandler] Linux signal handling initialized successfully.");
            else
                Debug.LogWarning("[SignalHandler] Failed to initialize Linux signal handling.");
        }
        catch (Exception ex)
        {
            Debug.LogError($"[SignalHandler] Exception during Linux initialization: {ex.Message}");
        }
#endif
    }

    private static void InitializeWindowsSignalHandling()
    {
        try
        {
            Console.CancelKeyPress += OnConsoleCancelKeyPress;
            Debug.Log("[SignalHandler] Windows signal handling initialized successfully.");
        }
        catch (Exception ex)
        {
            Debug.LogError($"[SignalHandler] Exception during Windows initialization: {ex.Message}");
        }
    }

    private static void OnConsoleCancelKeyPress(object sender, ConsoleCancelEventArgs e)
    {
        string signalName = e.SpecialKey == ConsoleSpecialKey.ControlC ? "CTRL+C" : "CTRL+Break";
        Debug.Log($"[SignalHandler] Received signal: {signalName}");
        e.Cancel = true;
        if (GameManager.instance != null)
            GameManager.instance.RequestGracefulShutdown(signalName);
        else
            Application.Quit(0);
    }

    private static void OnSignalReceived(int signalNumber)
    {
        string signalName = signalNumber switch
        {
            15 => "SIGTERM",
            2  => "SIGINT",
            1  => "SIGHUP",
            3  => "SIGQUIT",
            _  => "UNKNOWN"
        };
        Debug.Log($"[SignalHandler] Received signal: {signalName} ({signalNumber})");
        if (GameManager.instance != null)
            GameManager.instance.RequestGracefulShutdown(signalName);
        else
            Application.Quit(0);
    }
}