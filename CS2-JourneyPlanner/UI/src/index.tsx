import { ModRegistrar } from "cs2/modding";
import { JourneyPlanner } from "./journey-planner";

const register: ModRegistrar = (moduleRegistry) => {
  moduleRegistry.append("GameTopRight", JourneyPlanner);
};

export default register;
