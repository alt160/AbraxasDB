const commercialEditionDialog = document.querySelector("#commercial-edition-dialog"),
  commercialEditionFeature = document.querySelector("#commercial-edition-feature");

// Keep application-level navigation/search/Escape handlers behind the modal.
// Native dialog handling still supplies keyboard focus containment and dismissal.
commercialEditionDialog.addEventListener("keydown", (event) => event.stopPropagation());

/**
 * Explains a blocked commercial action without dispatching a native command or
 * changing the selected scenario, execution proof, or prepared workspace.
 * Repeated requests reuse the same modal instead of stacking dialogs.
 * @param {string} featureTitle The selected scenario or group title, rendered as text.
 */
function openCommercialEditionNotice(featureTitle) {
  commercialEditionFeature.textContent = featureTitle || "This example";
  if (!commercialEditionDialog.open) commercialEditionDialog.showModal();
}
