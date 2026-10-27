using Windows.Networking.Connectivity;

namespace DeskBox.Services;

/// <summary>
/// Metered-connection detection for the background auto-download (a direct
/// port of PowerToys' runner check): WWAN profiles, roaming, over-limit, and
/// fixed/variable cost networks all count as metered, and an auto-download
/// must never silently burn a cellular data plan.
/// </summary>
public static class UpdateNetworkCostPolicy
{
    public static bool IsMeteredConnection()
    {
        try
        {
            ConnectionProfile? profile = NetworkInformation.GetInternetConnectionProfile();
            if (profile is null)
            {
                return false;
            }

            if (profile.IsWwanConnectionProfile)
            {
                return true;
            }

            ConnectionCost cost = profile.GetConnectionCost();
            return cost.Roaming ||
                cost.OverDataLimit ||
                cost.NetworkCostType is NetworkCostType.Fixed or NetworkCostType.Variable;
        }
        catch (Exception)
        {
            // Profile APIs can throw during connectivity transitions; a
            // transient failure must not disable auto-download outright.
            return false;
        }
    }
}
