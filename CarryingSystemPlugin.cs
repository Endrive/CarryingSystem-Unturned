using Rocket.Core.Plugins;

namespace CarryingSystem
{
    public class CarryingSystemPlugin : RocketPlugin
    {
        public static CarryingSystemPlugin Instance { get; private set; }

        protected override void Load()
        {
            Instance = this;
            gameObject.AddComponent<CarryingInteraction>();
        }

        protected override void Unload()
        {
            var comp = gameObject.GetComponent<CarryingInteraction>();
            if (comp != null) Destroy(comp);
            
            Instance = null;
        }
    }
}
