import React from "react";
import { bindValue, trigger, useValue } from "cs2/api";
import "./journey-planner.scss";

const bindingGroup = "JourneyPlanner";

const connectionStatus$ = bindValue<string>(
  bindingGroup,
  "ConnectionStatus",
  "Waiting for C#..."
);

const contactCount$ = bindValue<number>(
  bindingGroup,
  "ContactCount",
  0
);

export const JourneyPlanner = () => {
  const connectionStatus = useValue(connectionStatus$);
  const contactCount = useValue(contactCount$);

  const testContact = () => {
    console.log("[JourneyPlanner] Sending TestContact trigger");

    trigger(
      bindingGroup,
      "TestContact"
    );
  };

  const resetContact = () => {
    console.log("[JourneyPlanner] Sending ResetContact trigger");

    trigger(
      bindingGroup,
      "ResetContact"
    );
  };

  return (
    <div className="journey-planner">
      <div className="journey-planner__header">
        Journey Planner
      </div>

      <div className="journey-planner__body">
        <div className="journey-contact-test">
          <div className="journey-contact-test__title">
            React ↔ C# contact test
          </div>

          <div className="journey-contact-test__status">
            {connectionStatus}
          </div>

          <div className="journey-contact-test__count">
            Successful calls: {contactCount}
          </div>

          <div className="journey-planner__actions">
            <button
              className="journey-button"
              onClick={resetContact}
              disabled={contactCount === 0}
            >
              Reset
            </button>

            <button
              className="journey-button journey-button--primary"
              onClick={testContact}
            >
              Test C# contact
            </button>
          </div>
        </div>
      </div>
    </div>
  );
};