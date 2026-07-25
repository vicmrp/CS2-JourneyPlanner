import React from "react";
import {
  bindValue,
  trigger,
  useValue,
} from "cs2/api";

import "./journey-planner.scss";

type SelectionMode =
  | "none"
  | "start"
  | "destination";

type JourneyPointType =
  | "start"
  | "destination";

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

const startEntityType$ = bindValue<string>(
  bindingGroup,
  "StartEntityType",
  ""
);

const destinationEntityType$ = bindValue<string>(
  bindingGroup,
  "DestinationEntityType",
  ""
);

export const JourneyPlanner = () => {
  const selectionMode =
    useValue(selectionMode$);

  const hasStart =
    useValue(hasStart$);

  const hasDestination =
    useValue(hasDestination$);

  const status =
    useValue(status$);

  const startPosition =
    useValue(startPosition$);

  const destinationPosition =
    useValue(destinationPosition$);

  const startEntityType =
    useValue(startEntityType$);

  const destinationEntityType =
    useValue(destinationEntityType$);

  const selectStart = () => {
    console.log(
      "[JourneyPlanner] SelectStart"
    );

    trigger(
      bindingGroup,
      "SelectStart"
    );
  };

  const selectDestination = () => {
    console.log(
      "[JourneyPlanner] SelectDestination"
    );

    trigger(
      bindingGroup,
      "SelectDestination"
    );
  };

  const clearRoute = () => {
    console.log(
      "[JourneyPlanner] ClearRoute"
    );

    trigger(
      bindingGroup,
      "ClearRoute"
    );
  };

  const calculateRoute = () => {
    console.log(
      "[JourneyPlanner] CalculateRoute"
    );

    trigger(
      bindingGroup,
      "CalculateRoute"
    );
  };

  const bothPointsSelected =
    hasStart && hasDestination;

  return (
    <div className="journey-planner">
      <div className="journey-planner__header">
        Journey Planner
      </div>

      <div className="journey-planner__body">
        <div className="journey-planner__status">
          {status}
        </div>

        <JourneyPoint
          type="start"
          label="Start"
          hasPoint={hasStart}
          position={startPosition}
          entityType={startEntityType}
          isSelecting={
            selectionMode === "start"
          }
          onSelect={selectStart}
        />

        <JourneyPoint
          type="destination"
          label="Destination"
          hasPoint={hasDestination}
          position={destinationPosition}
          entityType={destinationEntityType}
          isSelecting={
            selectionMode === "destination"
          }
          onSelect={selectDestination}
        />

        <div className="journey-planner__actions">
          <button
            type="button"
            className="journey-button"
            onClick={clearRoute}
          >
            Clear
          </button>

          <button
            type="button"
            className={
              "journey-button " +
              "journey-button--primary"
            }
            onClick={calculateRoute}
            disabled={!bothPointsSelected}
          >
            Calculate route
          </button>
        </div>
      </div>
    </div>
  );
};

interface JourneyPointProps {
  type: JourneyPointType;
  label: string;
  hasPoint: boolean;
  position: string;
  entityType: string;
  isSelecting: boolean;
  onSelect: () => void;
}

const JourneyPoint = ({
  type,
  label,
  hasPoint,
  position,
  entityType,
  isSelecting,
  onSelect,
}: JourneyPointProps) => {
  const buttonClassName = isSelecting
    ? "journey-button journey-button--active"
    : "journey-button";

  let buttonText = "Select";

  if (isSelecting) {
    buttonText = "Click on map…";
  } else if (hasPoint) {
    buttonText = "Change";
  }

  return (
    <div className="journey-point">
      <div
        className={
          "journey-point__marker " +
          `journey-point__marker--${type}`
        }
      />

      <div className="journey-point__content">
        <div className="journey-point__label">
          {label}
        </div>

        <div className="journey-point__status">
          {hasPoint
            ? position || "Point selected"
            : "No point selected"}
        </div>

        {hasPoint && entityType && (
          <div className="journey-point__entity-type">
            {entityType}
          </div>
        )}
      </div>

      <button
        type="button"
        className={buttonClassName}
        onClick={onSelect}
      >
        {buttonText}
      </button>
    </div>
  );
};