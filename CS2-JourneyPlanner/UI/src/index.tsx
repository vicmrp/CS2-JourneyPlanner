import { ModRegistrar } from "cs2/modding";
import { JourneyPlanner } from "mods/journey-planner";

const register: ModRegistrar = (moduleRegistry) => {
  console.log("[JourneyPlanner] UI module registered");

  moduleRegistry.append("Game", JourneyPlanner);
};

export default register;