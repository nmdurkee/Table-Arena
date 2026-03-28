using System;
using System.Collections.Generic;
using System.Linq;
using EchoVRAPI;
using Nevr.Telemetry.V2;
using Google.Protobuf.WellKnownTypes;
using UnityEngine;

namespace Tape
{
    /// <summary>
    /// Converts tape v2 protobuf frames to EchoVRAPI.Frame for playback.
    /// </summary>
    public static class TapeFrameConverter
    {
        // PlayerState.flags bit positions
        private const uint FlagStunned = 1 << 0;
        private const uint FlagInvulnerable = 1 << 1;
        private const uint FlagBlocking = 1 << 2;
        private const uint FlagPossession = 1 << 3;
        private const uint FlagEmotePlaying = 1 << 4;
        private const uint FlagTeamMask = 0x3 << 5; // bits 5-6
        private const int FlagTeamShift = 5;

        private static readonly Dictionary<GameStatus, string> GameStatusStrings = new Dictionary<GameStatus, string>
        {
            { GameStatus.Unspecified, "" },
            { GameStatus.PreMatch, "pre_match" },
            { GameStatus.RoundStart, "round_start" },
            { GameStatus.Playing, "playing" },
            { GameStatus.Score, "score" },
            { GameStatus.RoundOver, "round_over" },
            { GameStatus.PostMatch, "post_match" },
            { GameStatus.PreSuddenDeath, "pre_sudden_death" },
            { GameStatus.SuddenDeath, "sudden_death" },
            { GameStatus.PostSuddenDeath, "post_sudden_death" },
        };

        private static readonly Dictionary<MatchType, string> MatchTypeStrings = new Dictionary<MatchType, string>
        {
            { MatchType.Unspecified, "" },
            { MatchType.SocialPublic, "Social (Public)" },
            { MatchType.SocialPrivate, "Social (Private)" },
            { MatchType.Arena, "Echo Arena" },
            { MatchType.Combat, "Echo Combat" },
            { MatchType.EchoPass, "Echo Pass" },
            { MatchType.Ffa, "FFA" },
            { MatchType.Private, "Echo Arena Private" },
            { MatchType.Tournament, "Echo Arena Tournament" },
        };

        private static readonly Dictionary<PauseState, string> PauseStateStrings = new Dictionary<PauseState, string>
        {
            { PauseState.Unspecified, "unpaused" },
            { PauseState.NotPaused, "unpaused" },
            { PauseState.Paused, "paused" },
            { PauseState.Unpausing, "unpausing" },
            { PauseState.AutopauseReplay, "paused" },
        };

        /// <summary>
        /// Converts a tape v2 Frame + header context into an EchoVRAPI.Frame.
        /// The roster dictionary maps slot -> PlayerInfo and should be maintained
        /// by the caller across frames (updated with PlayerJoined events).
        /// </summary>
        public static EchoVRAPI.Frame Convert(Nevr.Telemetry.V2.Frame tapeFrame, CaptureHeader header,
            DateTime baseTime, Dictionary<int, PlayerInfo> roster)
        {
            if (tapeFrame == null)
                return null;

            var arena = tapeFrame.EchoArena;
            if (arena == null)
                return null;

            var arenaHeader = header?.EchoArena;

            if (roster == null)
                roster = new Dictionary<int, PlayerInfo>();

            // Group players by team using flags
            var bluePlayers = new List<Player>();
            var orangePlayers = new List<Player>();
            var spectatorPlayers = new List<Player>();

            int possessionTeam = -1;
            int possessionPlayer = -1;

            if (arena.Players != null)
            {
                foreach (var ps in arena.Players)
                {
                    int teamIndex = (int)((ps.Flags & FlagTeamMask) >> FlagTeamShift);
                    bool hasPossession = (ps.Flags & FlagPossession) != 0;

                    roster.TryGetValue(ps.Slot, out var info);
                    var player = ConvertPlayer(ps, info);

                    if (hasPossession)
                    {
                        possessionTeam = teamIndex;
                    }

                    switch (teamIndex)
                    {
                        case 0:
                            if (hasPossession) possessionPlayer = bluePlayers.Count;
                            player.team_color = EchoVRAPI.Team.TeamColor.blue;
                            bluePlayers.Add(player);
                            break;
                        case 1:
                            if (hasPossession) possessionPlayer = orangePlayers.Count;
                            player.team_color = EchoVRAPI.Team.TeamColor.orange;
                            orangePlayers.Add(player);
                            break;
                        default:
                            player.team_color = EchoVRAPI.Team.TeamColor.spectator;
                            spectatorPlayers.Add(player);
                            break;
                    }
                }
            }

            var teams = new List<EchoVRAPI.Team>
            {
                new EchoVRAPI.Team
                {
                    team = "BLUE TEAM",
                    color = EchoVRAPI.Team.TeamColor.blue,
                    possession = possessionTeam == 0,
                    players = bluePlayers,
                    stats = new Stats()
                },
                new EchoVRAPI.Team
                {
                    team = "ORANGE TEAM",
                    color = EchoVRAPI.Team.TeamColor.orange,
                    possession = possessionTeam == 1,
                    players = orangePlayers,
                    stats = new Stats()
                },
                new EchoVRAPI.Team
                {
                    team = "SPECTATORS",
                    color = EchoVRAPI.Team.TeamColor.spectator,
                    possession = false,
                    players = spectatorPlayers,
                    stats = new Stats()
                }
            };

            // Compute frame timestamp from base time + offset
            DateTime frameTime = baseTime.AddMilliseconds(tapeFrame.TimestampOffsetMs);

            // Build possession list
            var possession = new List<int>();
            if (possessionTeam >= 0 && possessionPlayer >= 0)
            {
                possession.Add(possessionTeam);
                possession.Add(possessionPlayer);
            }

            // Extract last score and last throw from events
            LastScore lastScore = new LastScore();
            LastThrow lastThrow = new LastThrow();
            if (arena.Events != null)
            {
                foreach (var evt in arena.Events)
                {
                    if (evt.EventCase == EchoEvent.EventOneofCase.GoalScored)
                    {
                        var gs = evt.GoalScored;
                        lastScore = new LastScore
                        {
                            disc_speed = gs.DiscSpeed,
                            team = gs.Team == Role.BlueTeam ? "blue" : "orange",
                            goal_type = gs.GoalType.ToString(),
                            point_amount = (int)gs.PointAmount,
                            distance_thrown = gs.DistanceThrown,
                            person_scored = gs.PersonScored,
                            assist_scored = gs.AssistScored
                        };
                    }
                    if (evt.EventCase == EchoEvent.EventOneofCase.DiscThrown && evt.DiscThrown.Details != null)
                    {
                        var td = evt.DiscThrown.Details;
                        lastThrow = new LastThrow
                        {
                            arm_speed = td.ArmSpeed,
                            total_speed = td.TotalSpeed,
                            off_axis_spin_deg = td.OffAxisSpinDeg,
                            wrist_throw_penalty = td.WristThrowPenalty,
                            rot_per_sec = td.RotPerSec,
                            pot_speed_from_rot = td.PotSpeedFromRot,
                            speed_from_arm = td.SpeedFromArm,
                            speed_from_movement = td.SpeedFromMovement,
                            speed_from_wrist = td.SpeedFromWrist,
                            wrist_align_to_throw_deg = td.WristAlignToThrowDeg,
                            throw_align_to_movement_deg = td.ThrowAlignToMovementDeg,
                            off_axis_penalty = td.OffAxisPenalty,
                            throw_move_penalty = td.ThrowMovePenalty
                        };
                    }
                }
            }

            var frame = new EchoVRAPI.Frame
            {
                frame_index = tapeFrame.FrameIndex,
                recorded_time = frameTime,

                // Session metadata from header
                sessionid = arenaHeader?.SessionId ?? "",
                match_type = arenaHeader != null ? MatchTypeStrings.GetValueOrDefault(arenaHeader.MatchType, "") : "",
                map_name = arenaHeader?.MapName ?? "",
                private_match = arenaHeader?.PrivateMatch ?? false,
                tournament_match = arenaHeader?.TournamentMatch ?? false,
                client_name = arenaHeader?.ClientName ?? "",

                // Per-frame game state
                game_status = GameStatusStrings.GetValueOrDefault(arena.GameStatus, ""),
                game_clock = arena.GameClock,
                game_clock_display = FormatGameClock(arena.GameClock),

                // Scores
                blue_points = arena.BluePoints,
                orange_points = arena.OrangePoints,
                total_round_count = arenaHeader?.TotalRoundCount ?? 0,

                // State
                possession = possession,
                disc = ConvertDisc(arena.Disc),
                last_score = lastScore,
                last_throw = lastThrow,
                pause = ConvertPause(arena.PauseState),
                player = ConvertVRRoot(arena.VrRoot),
                teams = teams
            };

            // Convert bones
            if (arena.PlayerBones != null && arena.PlayerBones.Count > 0)
            {
                frame.bones = ConvertBones(arena.PlayerBones);
            }

            return frame;
        }

        private static string FormatGameClock(float seconds)
        {
            if (seconds <= 0) return "00:00";
            int mins = (int)(seconds / 60f);
            int secs = (int)(seconds % 60f);
            return $"{mins:D2}:{secs:D2}";
        }

        private static Player ConvertPlayer(PlayerState ps, PlayerInfo info)
        {
            var player = new Player
            {
                playerid = ps.Slot,
                userid = info != null ? (long)info.AccountNumber : 0,
                name = info?.DisplayName ?? "",
                number = info?.JerseyNumber ?? 0,
                level = info?.Level ?? 0,
                ping = (int)ps.Ping,
                stunned = (ps.Flags & FlagStunned) != 0,
                invulnerable = (ps.Flags & FlagInvulnerable) != 0,
                blocking = (ps.Flags & FlagBlocking) != 0,
                possession = (ps.Flags & FlagPossession) != 0,
                is_emote_playing = (ps.Flags & FlagEmotePlaying) != 0,
                head = ConvertPose(ps.Head),
                body = ConvertPose(ps.Body),
                lhand = ConvertPose(ps.LeftHand),
                rhand = ConvertPose(ps.RightHand),
                velocity = ConvertVec3ToList(ps.Velocity),
                stats = new Stats()
            };

            return player;
        }

        private static EchoVRAPI.Transform ConvertPose(Nevr.Spatial.V1.Pose pose)
        {
            if (pose == null)
            {
                return new EchoVRAPI.Transform
                {
                    position = new List<float> { 0, 0, 0 },
                    forward = new List<float> { 0, 0, 1 },
                    left = new List<float> { 1, 0, 0 },
                    up = new List<float> { 0, 1, 0 }
                };
            }

            var t = new EchoVRAPI.Transform
            {
                position = new List<float>
                {
                    pose.Position?.X ?? 0,
                    pose.Position?.Y ?? 0,
                    pose.Position?.Z ?? 0
                },
                // Initialize with defaults, Rotation setter will overwrite
                forward = new List<float> { 0, 0, 1 },
                left = new List<float> { 1, 0, 0 },
                up = new List<float> { 0, 1, 0 }
            };

            if (pose.Orientation != null)
            {
                t.Rotation = new Quaternion(
                    pose.Orientation.X,
                    pose.Orientation.Y,
                    pose.Orientation.Z,
                    pose.Orientation.W
                );
            }

            return t;
        }

        private static EchoVRAPI.Disc ConvertDisc(DiscState disc)
        {
            if (disc == null)
                return EchoVRAPI.Disc.CreateEmpty();

            var d = new EchoVRAPI.Disc
            {
                position = new List<float>
                {
                    disc.Pose?.Position?.X ?? 0,
                    disc.Pose?.Position?.Y ?? 0,
                    disc.Pose?.Position?.Z ?? 0
                },
                velocity = ConvertVec3ToList(disc.Velocity),
                bounce_count = (int)disc.BounceCount,
                // Initialize with defaults, Rotation setter will overwrite
                forward = new List<float> { 0, 0, 1 },
                left = new List<float> { 1, 0, 0 },
                up = new List<float> { 0, 1, 0 }
            };

            if (disc.Pose?.Orientation != null)
            {
                d.Rotation = new Quaternion(
                    disc.Pose.Orientation.X,
                    disc.Pose.Orientation.Y,
                    disc.Pose.Orientation.Z,
                    disc.Pose.Orientation.W
                );
            }

            return d;
        }

        private static EchoVRAPI.VRPlayer ConvertVRRoot(Nevr.Spatial.V1.Pose vrRoot)
        {
            if (vrRoot == null)
                return EchoVRAPI.VRPlayer.CreateEmpty();

            var vr = new EchoVRAPI.VRPlayer
            {
                vr_position = new List<float>
                {
                    vrRoot.Position?.X ?? 0,
                    vrRoot.Position?.Y ?? 0,
                    vrRoot.Position?.Z ?? 0
                },
                vr_forward = new List<float> { 0, 0, 1 },
                vr_left = new List<float> { 1, 0, 0 },
                vr_up = new List<float> { 0, 1, 0 }
            };

            if (vrRoot.Orientation != null)
            {
                vr.Rotation = new Quaternion(
                    vrRoot.Orientation.X,
                    vrRoot.Orientation.Y,
                    vrRoot.Orientation.Z,
                    vrRoot.Orientation.W
                );
            }

            return vr;
        }

        private static Pause ConvertPause(PauseState pauseState)
        {
            return new Pause
            {
                paused_state = PauseStateStrings.GetValueOrDefault(pauseState, "unpaused"),
            };
        }

        private static List<float> ConvertVec3ToList(Nevr.Spatial.V1.Vec3 v)
        {
            if (v == null)
                return new List<float> { 0, 0, 0 };

            return new List<float> { v.X, v.Y, v.Z };
        }

        private static Bones ConvertBones(IList<Nevr.Telemetry.V2.PlayerBones> playerBones)
        {
            if (playerBones == null || playerBones.Count == 0)
                return null;

            var bones = new Bones
            {
                error_code = 0,
                user_bones = new BonePlayer[playerBones.Count]
            };

            for (int i = 0; i < playerBones.Count; i++)
            {
                var pb = playerBones[i];

                // transforms: packed little-endian float32, 3 floats per bone (x,y,z)
                float[] boneT = UnpackFloat32Bytes(pb.Transforms.ToByteArray());
                // orientations: packed little-endian float32, 4 floats per bone (x,y,z,w quat)
                float[] boneO = UnpackFloat32Bytes(pb.Orientations.ToByteArray());

                bones.user_bones[i] = new BonePlayer
                {
                    bone_t = boneT,
                    bone_o = boneO
                };
            }

            return bones;
        }

        private static float[] UnpackFloat32Bytes(byte[] data)
        {
            if (data == null || data.Length == 0)
                return Array.Empty<float>();

            int count = data.Length / 4;
            float[] result = new float[count];
            for (int i = 0; i < count; i++)
            {
                result[i] = BitConverter.ToSingle(data, i * 4);
            }
            return result;
        }
    }
}
