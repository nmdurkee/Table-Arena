using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.XR;
using UnityEngine.XR.Management;
using unityutilities;

public class GameManager : MonoBehaviour
{
	public static GameManager instance;

	public static bool quitting;

	public NetworkFrameManager netFrameMan;
	public Transform[] vrOnlyThings;
	public Transform[] flatOnlyThingsDesktop;
	public Transform[] flatOnlyThingsMobile;
	public Transform[] uiHiddenOnLive;
	public Transform[] uiShownOnLive;

	public string[] arenaModelScenes;

	public static readonly Dictionary<string, string> combatMapScenes = new Dictionary<string, string>
	{
		{"mpl_combat_dyson", "Dyson"},
		{"mpl_combat_combustion", "Combustion"},
		{"mpl_combat_fission", "Fission"},
		{"mpl_combat_gauss", "Surge"},
	};
	

	public Text dataSource;
	public Button becomeHostButton;
	[ReadOnly] public bool lastFrameWasOwner = true;
	[ReadOnly] public bool lastFrameUserPresent;
	[ReadOnly] public bool usingVR = false;
	public bool enableVR = false;
	public Rig vrRig;
	public Camera vrCamera;
	public Camera flatCameraDesktop;
	public Camera flatCameraMobile;

	public new Camera camera
	{
		get
		{
			return usingVR
				? vrCamera
#if (UNITY_IOS || UNITY_ANDROID) && !UNITY_EDITOR
				: flatCameraMobile;
#else
				: flatCameraDesktop;
#endif
		}
	}

	public DemoStart demoStart;

	[Header("Drawing Mode")] public MonoBehaviour[] drawingModeEnabled;
	public MonoBehaviour[] drawingModeDisabled;
	private bool drawingMode;

	/// <summary>
	/// True for drawing, false for not drawing
	/// </summary>
	public bool DrawingMode
	{
		set
		{
			drawingMode = value;
			foreach (MonoBehaviour obj in drawingModeEnabled)
			{
				obj.enabled = value;
			}

			foreach (MonoBehaviour obj in drawingModeDisabled)
			{
				obj.enabled = !value;
			}
		}
		get => drawingMode;
	}
	
	[Header("Clipping Mode")] public GameObject clippingModeUI;
	
	private bool clippingMode;
	public bool ClippingMode
	{
		set
		{
			clippingMode = value;
			clippingModeUI.SetActive(value);
		}
		get => clippingMode;
	}

	private void Awake()
	{
		instance = this;

		RefreshVRObjectsVisibility(false);
		lastFrameWasOwner = true;

		// Initialize OS signal handling for graceful shutdown
		SignalHandler.Initialize();

		List<string> args = System.Environment.GetCommandLineArgs().ToList();
		
		// Print help and exit
		if (args.Contains("--help") || args.Contains("-h") || args.Contains("-?"))
		{
			PrintHelp();
			Application.Quit();
			#if UNITY_EDITOR
			UnityEditor.EditorApplication.isPlaying = false;
			#endif
			return;
		}

		// only set the file the first time the scene is loaded. This is a pretty dumb way to do this.
		if (Time.timeAsDouble < 5)
		{
			foreach (string arg in args.Where(arg => arg.Contains(".echoreplay") || arg.Contains(".butter") || arg.Contains(".nevrcap") || arg.Contains(".tape")))
			{
				PlayerPrefs.SetString("fileDirector", arg);
				Debug.Log($"[GameManager] Command-line argument detected: {arg}");
				break;
			}
		}

		DrawingMode = false;
		ClippingMode = false;

		// Enable VR Mode
		if (enableVR || args.Contains("-useVR"))
		{
			enableVR = true;
			//RefreshVRObjectsVisibility(GetPresence());

			// Initialize and start XR subsystems
			XRGeneralSettings.Instance.Manager.InitializeLoaderSync();
			XRGeneralSettings.Instance.Manager.StartSubsystems();
		}
		else
		{
			// XR not enabled - nothing to do since we disabled auto-init
		}

		RefreshVRObjectsVisibility(enableVR);
		
		


		// add file handling to registry for .echoreplay files
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
		FileAssociations.SetAssociation(
			".echoreplay",
			"EchoVR.Replay.Viewer",
			"Echo VR Replay File",
			Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Replay Viewer.exe"));
		
		FileAssociations.SetAssociation(
			".butter",
			"EchoVR.Replay.Viewer",
			"Echo VR Replay File",
			Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Replay Viewer.exe"));

		FileAssociations.SetAssociation(
			".tape",
			"EchoVR.Replay.Viewer",
			"Echo VR Replay File",
			Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Replay Viewer.exe"));
#endif
	}


	// Update is called once per frame
	private void Update()
	{
		//// check if headset is worn
		//bool isPresent = GetPresence();
		//if (lastFrameUserPresent != isPresent)
		//{
		//	RefreshVRObjectsVisibility(isPresent);
		//}
		//lastFrameUserPresent = isPresent;

		// hide UI when connected to another user
		if (netFrameMan != null)
		{
			if (lastFrameWasOwner != netFrameMan.IsLocalOrServer)
			{
				foreach (var item in instance.uiHiddenOnLive)
				{
					item.gameObject.SetActive(netFrameMan.IsLocalOrServer);
				}

				foreach (var item in instance.uiShownOnLive)
				{
					item.gameObject.SetActive(!netFrameMan.IsLocalOrServer);
				}
			}

			lastFrameWasOwner = netFrameMan.IsLocalOrServer;

			becomeHostButton.gameObject.SetActive(!netFrameMan.IsLocalOrServer);
			if (!netFrameMan.IsLocalOrServer)
			{
				dataSource.text = netFrameMan.networkFilename;
			}
		}
	}

	private void RefreshVRObjectsVisibility(bool present)
	{
		Debug.Log("RefreshVRObjectsVisibility: " + present);
		usingVR = present;
		if (present)
		{
			foreach (Transform thing in vrOnlyThings)
			{
				thing.gameObject.SetActive(true);
			}

			foreach (Transform thing in flatOnlyThingsDesktop)
			{
				thing.gameObject.SetActive(false);
			}

			foreach (Transform thing in flatOnlyThingsMobile)
			{
				thing.gameObject.SetActive(false);
			}
		}
		else
		{
			foreach (var thing in vrOnlyThings)
			{
				thing.gameObject.SetActive(false);
			}

			foreach (var thing in flatOnlyThingsDesktop)
			{
#if (UNITY_IOS || UNITY_ANDROID) && !UNITY_EDITOR
				thing.gameObject.SetActive(false);
#else
				thing.gameObject.SetActive(true);
#endif
			}

			foreach (var thing in flatOnlyThingsMobile)
			{
#if (UNITY_IOS || UNITY_ANDROID) && !UNITY_EDITOR
				thing.gameObject.SetActive(true);
#else
				thing.gameObject.SetActive(false);
#endif
			}
		}
	}

	/// <summary>
	/// Called by SignalHandler when the application receives a termination signal.
	/// Initiates a graceful shutdown with proper resource cleanup.
	/// </summary>
	public void RequestGracefulShutdown(string signalName)
	{
		Debug.Log($"[GameManager] Graceful shutdown requested via {signalName}");
		
		// Set quitting flag to prevent new operations
		quitting = true;
		
		// Cleanup resources before quitting
		CleanupResources();
		
		// Quit the application
		Debug.Log("[GameManager] Calling Application.Quit()");
		Application.Quit(0);
		
		#if UNITY_EDITOR
		// In editor, stop play mode
		UnityEditor.EditorApplication.isPlaying = false;
		#endif
	}

	/// <summary>
	/// Cleanup resources before application shutdown.
	/// </summary>
	private void CleanupResources()
	{
		Debug.Log("[GameManager] Cleaning up resources...");
		
		try
		{
			// Stop all coroutines to prevent hanging
			Debug.Log("[GameManager] Stopping all coroutines");
			StopAllCoroutines();
			
			// Also stop DemoStart coroutines (like file loading)
			if (demoStart != null)
			{
				Debug.Log("[GameManager] Stopping DemoStart coroutines");
				demoStart.StopAllCoroutines();
			}
		}
		catch (Exception ex)
		{
			Debug.LogWarning($"[GameManager] Error stopping coroutines: {ex.Message}");
		}
		
		try
		{
			// Stop XR subsystems if VR is enabled
			if (enableVR && XRGeneralSettings.Instance != null && XRGeneralSettings.Instance.Manager != null)
			{
				Debug.Log("[GameManager] Stopping XR subsystems");
				XRGeneralSettings.Instance.Manager.StopSubsystems();
				XRGeneralSettings.Instance.Manager.DeinitializeLoader();
			}
		}
		catch (Exception ex)
		{
			Debug.LogWarning($"[GameManager] Error during XR cleanup: {ex.Message}");
		}
		
		try
		{
			// Cleanup network manager if present
			if (netFrameMan != null)
			{
				Debug.Log("[GameManager] Cleaning up network manager");
				// Network manager cleanup (if it has any cleanup methods)
			}
		}
		catch (Exception ex)
		{
			Debug.LogWarning($"[GameManager] Error during network cleanup: {ex.Message}");
		}
		
		Debug.Log("[GameManager] Resource cleanup complete");
	}

	private void OnApplicationQuit()
	{
		Debug.Log("[GameManager] OnApplicationQuit called");
		quitting = true;
		
		// Ensure cleanup happens even if quit wasn't via RequestGracefulShutdown
		CleanupResources();
	}
	
	private void PrintHelp()
	{
		string helpText = @"
EchoVR Demo Viewer - Replay file viewer for .echoreplay, .butter, .nevrcap, and .tape files

USAGE:
  Demo Viewer.exe [OPTIONS] [REPLAY_FILE]

OPTIONS:
  --help, -h, -?     Show this help message
  -useVR             Launch in VR mode (OpenXR)

SUPPORTED FILE FORMATS:
  .echoreplay        JSON-based replay format (uncompressed or gzip)
  .butter            Binary compressed replay format
  .nevrcap           Zstd-compressed protobuf capture format (v1)
  .tape              Zstd-compressed protobuf capture format (v2)

EXAMPLES:
  Demo Viewer.exe myreplay.echoreplay
  Demo Viewer.exe -useVR myreplay.butter
  Demo Viewer.exe recording.nevrcap
  Demo Viewer.exe recording.tape

DEBUGGING:
  To see console output on Windows, run with:
    Demo Viewer.exe -logFile output.log
  Or check the Player.log file at:
    %USERPROFILE%\AppData\LocalLow\<CompanyName>\<ProductName>\Player.log

For more information, visit: https://github.com/EchoTools/Demo-Viewer
";
		Debug.Log(helpText);
		// Also write to console for standalone builds
		System.Console.WriteLine(helpText);
	}
}