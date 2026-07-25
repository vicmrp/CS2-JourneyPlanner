import React from "react";
import { bindValue, trigger, useValue } from "cs2/api";
import "./journey-planner.scss";

type SelectionMode = "none" | "start" | "destination";

const bindingGroup = "JourneyPlanner";

const selectionMode$ = bindValue<SelectionMode>(
  bindingGroup,
  "SelectionMode",
  "none"
);

const hasStart$ = bindValue<boolean>(
  bindingGroup,
  "HasStart",
  false
);

const hasDestination$ = bindValue<boolean>(
  bindingGroup,
  "HasDestination",
  false
);

const status$ = bindValue<string>(
  bindingGroup,
  "Status",
  "Select a starting point"
);

const startPosition$ = bindValue<string>(
  bindingGroup,
  "StartPosition",
  ""
);

const destinationPosition$ = bindValue<string>(
  bindingGroup,
  "DestinationPosition",
  ""
);

export const JourneyPlanner = () => {
  const selectionMode = useValue(selectionMode$);
  const hasStart = useValue(hasStart$);
  const hasDestination = useValue(hasDestination$);
  const status = useValue(status$);
  const startPosition = useValue(startPosition$);
  const destinationPosition = useValue(destinationPosition$);

  const selectStart = () => {
    console.log(
      "[JourneyPlanner] Sending SelectStart trigger"
    );

    trigger(bindingGroup, "SelectStart");
  };

  const selectDestination = () => {
    console.log(
      "[JourneyPlanner] Sending SelectDestination trigger"
    );

    trigger(bindingGroup, "SelectDestination");
  };

  const clearRoute = () => {
    console.log(
      "[JourneyPlanner] Sending ClearRoute trigger"
    );

    trigger(bindingGroup, "ClearRoute");
  };

  const calculateRoute = () => {
    console.log(
      "[JourneyPlanner] Sending CalculateRoute trigger"
    );

    trigger(bindingGroup, "CalculateRoute");
  };

  return (
    <div className="journey-planner">
      <div className="journey-planner__header">
        Journey Planner
      </div>

      <div className="journey-planner__body">
        <div className="journey-planner__status">
          {status}
        </div>

        <div className="journey-point">
          <div
            className={
              "journey-point__marker " +
              "journey-point__marker--start"
            }
          />

          <div className="journey-point__content">
            <div className="journey-point__label">
              Start
            </div>

            <div className="journey-point__status">
              {hasStart
                ? startPosition || "Point selected"
                : "No point selected"}
            </div>
          </div>

          <button
            className={
              selectionMode === "start"
                ? "journey-button journey-button--active"
                : "journey-button"
            }
            onClick={selectStart}
          >
            {selectionMode === "start"
              ? "Click on map…"
              : hasStart
                ? "Change"
                : "Select"}
          </button>
        </div>

        <div className="journey-point">
          <div
            className={
              "journey-point__marker " +
              "journey-point__marker--destination"
            }
          />

          <div className="journey-point__content">
            <div className="journey-point__label">
              Destination
            </div>

            <div className="journey-point__status">
              {hasDestination
                ? destinationPosition || "Point selected"
                : "No point selected"}
            </div>
          </div>

          <button
            className={
              selectionMode === "destination"
                ? "journey-button journey-button--active"
                : "journey-button"
            }
            onClick={selectDestination}
          >
            {selectionMode === "destination"
              ? "Click on map…"
              : hasDestination
                ? "Change"
                : "Select"}
          </button>
        </div>

        <div className="journey-planner__actions">
          <button
            className="journey-button"
            onClick={clearRoute}
          >
            Clear
          </button>

          <button
            className={
              "journey-button " +
              "journey-button--primary"
            }
            onClick={calculateRoute}
            disabled={!hasStart || !hasDestination}
          >
            Calculate route
          </button>
        </div>
      </div>
    </div>
  );
};