using RimWorld;
using Verse;

namespace RimMercenaries
{
    // Minimal worker to satisfy IncidentDef.Worker instantiation.
    // Never fires and performs no actions.
    public class IncidentWorker_MercenaryAcceptedDummy : IncidentWorker
    {
        protected override bool CanFireNowSub(IncidentParms parms)
        {
            return false;
        }

        protected override bool TryExecuteWorker(IncidentParms parms)
        {
            return false;
        }
    }
}

