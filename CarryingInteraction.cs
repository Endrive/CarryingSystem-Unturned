using Rocket.Unturned.Player;
using SDG.Unturned;
using UnityEngine;
using Steamworks;
using System.Collections.Generic;
using System.Collections;
using Rocket.Unturned.Chat;

namespace CarryingSystem
{
    public class CarryingInteraction : MonoBehaviour
    {
        private static CarryingInteraction _instance;

        private Dictionary<ulong, ulong> carryingPlayers = new Dictionary<ulong, ulong>();
        private Dictionary<ulong, ulong> vehiclePassengers = new Dictionary<ulong, ulong>();
        private HashSet<ulong> _beingLoaded = new HashSet<ulong>();
        private Dictionary<ulong, float> _gestureCooldown = new Dictionary<ulong, float>();
        private List<ulong> _reusableRemoveList = new List<ulong>();
        private float lastUpdate = 0f;

        void Start()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(this);
                return;
            }
            _instance = this;

            PlayerAnimator.OnGestureChanged_Global += OnGestureChanged;
            VehicleManager.onEnterVehicleRequested += OnEnterVehicleRequested;
            VehicleManager.onExitVehicleRequested += OnExitVehicleRequested;
        }

        void OnDestroy()
        {
            if (_instance == this) _instance = null;
            PlayerAnimator.OnGestureChanged_Global -= OnGestureChanged;
            VehicleManager.onEnterVehicleRequested -= OnEnterVehicleRequested;
            VehicleManager.onExitVehicleRequested -= OnExitVehicleRequested;
        }

        private bool IsCuffed(ulong steamID)
        {
            UnturnedPlayer p = UnturnedPlayer.FromCSteamID(new CSteamID(steamID));
            if (p == null || p.Player == null) return false;
            return p.Player.animator.gesture == EPlayerGesture.ARREST_START;
        }

        private void OnEnterVehicleRequested(Player player, InteractableVehicle vehicle, ref bool shouldAllow)
        {
            if (!shouldAllow) return;

            ulong playerID = player.channel.owner.playerID.steamID.m_SteamID;

            if (IsCuffed(playerID) && !_beingLoaded.Contains(playerID))
            {
                shouldAllow = false;
                UnturnedPlayer cuffedPlayer = UnturnedPlayer.FromCSteamID(new CSteamID(playerID));
                if (cuffedPlayer != null)
                    UnturnedChat.Say(cuffedPlayer, "Jesteś zakuty i nie możesz samodzielnie wsiąść do pojazdu!", Color.red);
                return;
            }

            if (carryingPlayers.TryGetValue(playerID, out ulong cuffedID))
            {
                carryingPlayers.Remove(playerID);
                UnturnedPlayer cuffed = UnturnedPlayer.FromCSteamID(new CSteamID(cuffedID));
                if (cuffed != null)
                {
                    StartCoroutine(ForceIntoVehicleCoroutine(cuffed, vehicle, playerID));
                }
            }
        }

        private void OnExitVehicleRequested(Player player, InteractableVehicle vehicle, ref bool shouldAllow, ref Vector3 pendingLocation, ref float pendingYaw)
        {
            if (!shouldAllow) return;

            ulong playerID = player.channel.owner.playerID.steamID.m_SteamID;

            if (vehiclePassengers.TryGetValue(playerID, out ulong cuffedID))
            {
                vehiclePassengers.Remove(playerID);
                UnturnedPlayer cuffed = UnturnedPlayer.FromCSteamID(new CSteamID(cuffedID));
                if (cuffed != null && cuffed.Player.movement.getVehicle() == vehicle)
                {
                    VehicleManager.forceRemovePlayer(cuffed.CSteamID);
                    Vector3 exitPos = pendingLocation;
                    exitPos.x += 1.5f;
                    cuffed.Teleport(exitPos, pendingYaw);
                }
            }
        }

        private IEnumerator ForceIntoVehicleCoroutine(UnturnedPlayer cuffed, InteractableVehicle vehicle, ulong rescuerID)
        {
            ulong cuffedID = cuffed.CSteamID.m_SteamID;
            _beingLoaded.Add(cuffedID);

            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();

            UnturnedPlayer rescuer = UnturnedPlayer.FromCSteamID(new CSteamID(rescuerID));

            if (cuffed != null && vehicle != null && !cuffed.Player.life.isDead)
            {
                bool success = VehicleManager.ServerForcePassengerIntoVehicle(cuffed.Player, vehicle);
                _beingLoaded.Remove(cuffedID);

                if (success)
                {
                    vehiclePassengers[rescuerID] = cuffedID;
                    if (rescuer != null)
                        UnturnedChat.Say(rescuer, "Wsadziłeś zakutego do pojazdu.", Color.cyan);
                }
                else
                {
                    if (rescuer != null)
                        UnturnedChat.Say(rescuer, "Brak wolnych miejsc w tym pojeździe!", Color.red);
                }
            }
            else
            {
                _beingLoaded.Remove(cuffedID);
            }
        }

        private void OnGestureChanged(PlayerAnimator animator, EPlayerGesture gesture)
        {
            if (animator == null || animator.player == null) return;

            ulong animatorID = animator.player.channel.owner.playerID.steamID.m_SteamID;

            if (IsCuffed(animatorID)) return;

            UnturnedPlayer rescuer = UnturnedPlayer.FromPlayer(animator.player);
            if (rescuer == null) return;

            if (gesture == EPlayerGesture.SURRENDER_START)
            {
                ulong rescuerId = rescuer.CSteamID.m_SteamID;

                if (_gestureCooldown.TryGetValue(rescuerId, out float lastTime) && Time.time - lastTime < 0.5f)
                    return;
                _gestureCooldown[rescuerId] = Time.time;

                if (carryingPlayers.ContainsKey(rescuerId))
                {
                    carryingPlayers.Remove(rescuerId);
                    UnturnedChat.Say(rescuer, "Upuściłeś gracza.", Color.yellow);
                    return;
                }

                Player targetPlayer = null;

                if (Physics.Raycast(rescuer.Player.look.aim.position, rescuer.Player.look.aim.forward, out RaycastHit hit, 4f, RayMasks.PLAYER))
                {
                    targetPlayer = DamageTool.getPlayer(hit.transform) ?? hit.transform.GetComponentInParent<Player>();
                }

                if (targetPlayer == null || targetPlayer == rescuer.Player)
                {
                    Collider[] hitColliders = Physics.OverlapSphere(rescuer.Player.look.aim.position + rescuer.Player.look.aim.forward * 2f, 2.0f, RayMasks.PLAYER);
                    foreach (var hitCollider in hitColliders)
                    {
                        Player p = DamageTool.getPlayer(hitCollider.transform) ?? hitCollider.transform.GetComponentInParent<Player>();
                        if (p != null && p != rescuer.Player && p.animator.gesture == EPlayerGesture.ARREST_START)
                        {
                            targetPlayer = p;
                            break;
                        }
                    }
                }

                if (targetPlayer != null && targetPlayer != rescuer.Player)
                {
                    ulong targetID = targetPlayer.channel.owner.playerID.steamID.m_SteamID;
                    if (targetPlayer.animator.gesture == EPlayerGesture.ARREST_START)
                    {
                        carryingPlayers[rescuerId] = targetID;
                        UnturnedChat.Say(rescuer, $"Podniosłeś zakutego gracza {targetPlayer.channel.owner.playerID.characterName}!", Color.green);
                    }
                }
            }
        }

        void Update()
        {
            if (Time.time - lastUpdate < 0.1f) return;
            lastUpdate = Time.time;

            if (carryingPlayers.Count == 0) return;
            _reusableRemoveList.Clear();

            foreach (var kvp in carryingPlayers)
            {
                UnturnedPlayer rescuer = UnturnedPlayer.FromCSteamID(new CSteamID(kvp.Key));
                UnturnedPlayer cuffed = UnturnedPlayer.FromCSteamID(new CSteamID(kvp.Value));

                if (rescuer == null || cuffed == null)
                {
                    _reusableRemoveList.Add(kvp.Key);
                    continue;
                }

                if (cuffed.Player.animator.gesture != EPlayerGesture.ARREST_START)
                {
                    _reusableRemoveList.Add(kvp.Key);
                    continue;
                }

                Vector3 carryPosition = rescuer.Position;
                carryPosition.y += 0.5f;
                cuffed.Teleport(carryPosition, rescuer.Rotation);
            }

            foreach (var key in _reusableRemoveList) carryingPlayers.Remove(key);
        }
    }
}
