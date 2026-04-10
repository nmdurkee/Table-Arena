using System.Collections.Generic;
using EchoVRAPI;
using UnityEngine;
using UnityEngine.Serialization;
using Transform = UnityEngine.Transform;

public class PlayerV4 : MonoBehaviour
{
	private Player playerData;
	private BonePlayer bonePlayerData;

	[FormerlySerializedAs("body")] public Transform head;
	public Transform leftHand;
	public Transform rightHand;

	private GameObject skeleParent;
	private List<GameObject> flyingSkeleton = new List<GameObject>();

	public GameObject[] boneMapping = new GameObject[23];

	public void SetPlayerData(Team.TeamColor teamColor, Player player, BonePlayer bonePlayer)
	{
		playerData = player;
		bonePlayerData = bonePlayer;

		// Always set head, body, and hand positions from player data
		head.SetPositionAndRotation(player.head.Position, player.head.Rotation);
		leftHand.SetPositionAndRotation(player.lhand.Position, player.lhand.Rotation);
		rightHand.SetPositionAndRotation(player.rhand.Position, player.rhand.Rotation);

		Vector3 thisPos = player.body.Position;
		transform.SetPositionAndRotation(thisPos, player.body.Rotation);

		// If bone data is available, use it for full skeleton rendering
		// Check that arrays are not just present but have the expected data (92 quaternion values, 69 position values)
		if (bonePlayer != null && bonePlayer.bone_o != null && bonePlayer.bone_t != null &&
		    bonePlayer.bone_o.Length == 92 && bonePlayer.bone_t.Length == 69)
		{
			(Vector3, Quaternion)[] bones = bonePlayer.GetPoses();
			for (int i = 0; i < bones.Length; i++)
			{
				if (flyingSkeleton.Count <= i)
				{
					if (skeleParent == null)
					{
						skeleParent = new GameObject("Skeleton Parent");
						skeleParent.transform.SetParent(transform);
						skeleParent.transform.localEulerAngles = new Vector3(0, -90, 0);
						skeleParent.transform.localPosition = Vector3.zero;
					}

					var boneGO = new GameObject($"bone_{i}");
					boneGO.transform.SetParent(skeleParent.transform);
					flyingSkeleton.Add(boneGO);
				}

				flyingSkeleton[i].transform.localPosition = bones[i].Item1;
				flyingSkeleton[i].transform.localRotation = bones[i].Item2;

				if (boneMapping[i] != null)
				{
					boneMapping[i].transform.rotation = skeleParent.transform.rotation * bones[i].Item2;
				}
			}
		}
		else if (bonePlayer != null)
		{
			Debug.LogWarning($"[PlayerV4] Player {player.name} has invalid bone data: bone_o={(bonePlayer.bone_o?.Length ?? -1)}, bone_t={(bonePlayer.bone_t?.Length ?? -1)}. Expected bone_o=92, bone_t=69. Falling back to head/body/hands only.");
		}
	}

	private void Update()
	{
		// Only draw debug lines if bone data is available and valid
		if (bonePlayerData != null && bonePlayerData.bone_o != null && bonePlayerData.bone_t != null &&
		    bonePlayerData.bone_o.Length == 92 && bonePlayerData.bone_t.Length == 69)
		{
			(Vector3, Quaternion)[] bones = bonePlayerData.GetPoses();
			Debug.DrawLine(transform.TransformPoint(bones[2].Item1), transform.TransformPoint(bones[4].Item1));
			Debug.DrawLine(transform.TransformPoint(bones[4].Item1), transform.TransformPoint(bones[6].Item1));
			Debug.DrawLine(transform.TransformPoint(bones[3].Item1), transform.TransformPoint(bones[5].Item1));
			Debug.DrawLine(transform.TransformPoint(bones[5].Item1), transform.TransformPoint(bones[7].Item1));
		}
	}
}
