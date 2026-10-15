using System;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace LSBureaucracyBridge
{
    [KSPAddon(KSPAddon.Startup.SpaceCentre, true)]
    public class USILSBureaucracyBridgeLoader : MonoBehaviour
    {
        private void Start()
        {
			try
			{
				// Register to KSP's crew modification event
				GameEvents.onCrewModified.Add(OnCrewModified);
				DontDestroyOnLoad(this);
				Debug.Log("[LSBureaucracyBridge] Plugin initialized successfully.");
			}
			catch (Exception ex)
            {
                Debug.LogError($"[LSBureaucracyBridge] in USILSBureaucracyBridgeLoader.Start had: {ex.Message}");
            }
        }

        private void OnDestroy()
        {
			try
			{
				GameEvents.onCrewModified.Remove(OnCrewModified);
			}
			catch (Exception ex)
            {
                Debug.LogError($"[LSBureaucracyBridge] in USILSBureaucracyBridgeLoader.OnDestroy had: {ex.Message}");
            }
        }

        private void OnCrewModified(Vessel vessel)
        {
			try
			{
				if (vessel == null) return;

				foreach (ProtoCrewMember kerbal in vessel.GetVesselCrew())
				{
					// Check if USI Life Support turned the Kerbal into a Tourist or marked them as Mutinous
					if (kerbal.rosterStatus == ProtoCrewMember.RosterStatus.Available || kerbal.rosterStatus == ProtoCrewMember.RosterStatus.Assigned)
					{
						if (kerbal.type == ProtoCrewMember.KerbalType.Tourist)
						{
							// Verify if this tourist state is due to USI-LS by checking USI custom traits or statuses
							if (IsUSILifeSupportMutiny(kerbal))
							{
								Debug.Log($"[LSBureaucracyBridge] Detected USI Mutiny/Grouchy state on {kerbal.name}. Intercepting...");

								// 1. Revert their type back to Crew member so they don't stay a useless tourist
								RevertKerbalType(kerbal);

								// 2. Penalize their RetirementDate from Bureaucracy (e.g., subtract 1 Year / 426 Kerbal days)
								double penaltySeconds = KSPUtil.dateTimeFormatter.Year * 1.0; 
								ApplyRetirementPenalty(kerbal.name, penaltySeconds);
								
								// 3. Optional: Reset the USI timer slightly so it doesn't instantly re-trigger 
								ResetUSILSTimer(kerbal);
							}
						}
					}
				}
			}
			catch (Exception ex)
            {
                Debug.LogError($"[LSBureaucracyBridge] in USILSBureaucracyBridgeLoader.OnCrewModified had: {ex.Message}");
            }
        }

        private bool IsUSILifeSupportMutiny(ProtoCrewMember kerbal)
        {
            try
            {
                // Access USI Life Support scenario via reflection
                Type lsScenarioType = AssemblyLoader.loadedAssemblies
                    .SelectMany(a => a.assembly.GetTypes())
                    .FirstOrDefault(t => t.Name == "LifeSupportScenario");

                if (lsScenarioType == null) return false;

                PropertyInfo instanceProp = lsScenarioType.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static);
                object instance = instanceProp?.GetValue(null);
                if (instance == null) return false;

                // Check LifeSupportInfo or status dictionary inside USI
                FieldInfo infoField = lsScenarioType.GetField("LifeSupportInfo", BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public);
                System.Collections.IList list = infoField?.GetValue(instance) as System.Collections.IList;

                if (list != null)
                {
                    foreach (object item in list)
                    {
                        PropertyInfo nameProp = item.GetType().GetProperty("KerbalName");
                        string name = nameProp?.GetValue(item) as string;

                        if (name == kerbal.name)
                        {
                            // Check if they are currently under a negative effect
                            PropertyInfo isGrouchyProp = item.GetType().GetProperty("IsGrouchy");
                            bool isGrouchy = (bool)(isGrouchyProp?.GetValue(item) ?? false);
                            
                            return isGrouchy;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[LSBureaucracyBridge] in USILSBureaucracyBridgeLoader.IsUSILifeSupportMutiny had: {ex.Message}");
            }
            return false;
        }

        private void RevertKerbalType(ProtoCrewMember kerbal)
        {
			try
            {
				// Restore original class type if tracked, or default back to standard Crew
				kerbal.type = ProtoCrewMember.KerbalType.Crew;
				
				// Trigger UI refresh
				MonoBehaviour.print($"[LSBureaucracyBridge] Restored {kerbal.name} to active duty status.");
			}
			catch (Exception ex)
            {
                Debug.LogError($"[LSBureaucracyBridge] in RevertKerbalType had: {ex.Message}");
            }
        }

        private void ApplyRetirementPenalty(string kerbalName, double penaltySeconds)
        {
            try
            {
                // Locate the Bureaucracy Assembly and its Kerbal Manager/Scenario
                Type bureaucracyManagerType = AssemblyLoader.loadedAssemblies
                    .SelectMany(a => a.assembly.GetTypes())
                    .FirstOrDefault(t => t.Name == "KerbalManager" || t.Name == "BureaucracyScenario");

                if (bureaucracyManagerType == null)
                {
                    Debug.LogWarning("[LSBureaucracyBridge] Bureaucracy assembly or KerbalManager not found.");
                    return;
                }

                // Attempt to find the running instance or static container holding Kerbal info
                PropertyInfo instanceProp = bureaucracyManagerType.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static);
                object managerInstance = instanceProp?.GetValue(null);

                // Fetch the list or dictionary of tracked Kerbals in Bureaucracy
                FieldInfo kerbalsField = bureaucracyManagerType.GetField("repo", BindingFlags.NonPublic | BindingFlags.Instance) 
                                      ?? bureaucracyManagerType.GetField("kerbals", BindingFlags.NonPublic | BindingFlags.Instance);
                
                System.Collections.IDictionary kerbalRepo = kerbalsField?.GetValue(managerInstance) as System.Collections.IDictionary;

                if (kerbalRepo != null && kerbalRepo.Contains(kerbalName))
                {
                    object kerbalInfo = kerbalRepo[kerbalName];
                    PropertyInfo retirementProp = kerbalInfo.GetType().GetProperty("RetirementDate", BindingFlags.Public | BindingFlags.Instance);

                    if (retirementProp != null)
                    {
                        double currentRetirement = (double)retirementProp.GetValue(kerbalInfo);
                        double newRetirement = currentRetirement - penaltySeconds;
                        
                        // Enforce that retirement cannot drop past the current game time
                        if (newRetirement < Planetarium.GetUniversalTime())
                            newRetirement = Planetarium.GetUniversalTime() + 100;

                        retirementProp.SetValue(kerbalInfo, newRetirement);
                        Debug.Log($"[LSBureaucracyBridge] Reduced {kerbalName}'s retirement date by {penaltySeconds} seconds. New time: {newRetirement}");
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[LSBureaucracyBridge] in USILSBureaucracyBridgeLoader.ApplyRetirementPenalty had: {ex.Message}");
            }
        }

        private void ResetUSILSTimer(ProtoCrewMember kerbal)
        {
            try
            {
                // Resets USI timers forward so it doesn't evaluate a mutiny condition on the next physics tick
                Type lsScenarioType = AssemblyLoader.loadedAssemblies.SelectMany(a => a.assembly.GetTypes()).FirstOrDefault(t => t.Name == "LifeSupportScenario");
                object instance = lsScenarioType?.GetProperty("Instance")?.GetValue(null);
                System.Collections.IList list = lsScenarioType?.GetField("LifeSupportInfo")?.GetValue(instance) as System.Collections.IList;

                if (list != null)
                {
                    foreach (object item in list)
                    {
                        if ((string)item.GetType().GetProperty("KerbalName")?.GetValue(item) == kerbal.name)
                        {
                            // Reset LastMeal / ExtendTime forward to give them a brief buffer zone
                            PropertyInfo lastMealProp = item.GetType().GetProperty("LastMeal");
                            lastMealProp?.SetValue(item, Planetarium.GetUniversalTime());
                            break;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[LSBureaucracyBridge] in USILSBureaucracyBridgeLoader.ResetUSILSTimer had: {ex.Message}");
            }
        }
    }
}
