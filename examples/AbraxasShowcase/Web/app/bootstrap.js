cancelCreationButton.onclick = closeCreationDialog;
acceptCreationButton.onclick = acceptCreationAction;
lightbox.querySelector(".creation-backdrop").onclick = closeCreationDialog;
conceptClose.onclick = closeConceptDetails;
conceptCloseIcon.onclick = closeConceptDetails;
conceptLightbox.querySelector(".concept-backdrop").onclick =
  closeConceptDetails;
typeDefinitionClose.onclick = closeTypeDefinition;
typeDefinitionPopover.addEventListener("toggle", () => {
  if (!typeDefinitionPopover.matches(":popover-open"))
    disposeTypeDefinitionEditor();
});
typeDefinitionPopover
  .querySelector(".type-definition-head")
  .addEventListener("pointerdown", beginTypeDefinitionDrag);
typeDefinitionPopover
  .querySelector(".type-definition-head")
  .addEventListener("pointermove", moveTypeDefinitionDrag);
typeDefinitionPopover
  .querySelector(".type-definition-head")
  .addEventListener("pointerup", endTypeDefinitionDrag);
typeDefinitionPopover
  .querySelector(".type-definition-head")
  .addEventListener("pointercancel", endTypeDefinitionDrag);
document.addEventListener("keydown", (event) => {
  if (
    event.altKey &&
    (event.key === "ArrowLeft" || event.key === "ArrowRight")
  ) {
    event.preventDefault();
    traverseHistory(event.key === "ArrowLeft" ? -1 : 1);
    return;
  }
  if (event.key !== "Escape") return;
  const explorers = [...document.querySelectorAll(".floating-explorer-window")];
  if (explorers.length) explorers.at(-1).remove();
  else if (!conceptLightbox.hidden) closeConceptDetails();
  else if (!lightbox.hidden) closeCreationDialog();
});
navigationBack.onclick = () => traverseHistory(-1);
navigationForward.onclick = () => traverseHistory(1);
wireRailResizer();
document.querySelectorAll(".nav").forEach(
  (button, index) =>
    (button.onclick = () => {
      if (button.id === "what-if-toggle") {
        toggleWhatIfNavigation();
        return;
      }
      const buttons = [...document.querySelectorAll(".nav")],
        activeIndex = buttons.findIndex((item) =>
          item.classList.contains("active"),
        ),
        motion = activeIndex < 0 ? "fade" : index > activeIndex ? "up" : "down";
      if (button.dataset.view === "catalog") {
        catalogCategoryFilter = null;
        catalogTargetScenarioId = null;
        expandedCatalogCategories.clear();
      }
      navigateTo({ view: button.dataset.view }, motion);
    }),
);
(async () => {
  try {
    state = await call("get-state");
    wireGlobalSearch();
    navigationHistory.push({ route: { view: "home" }, motion: "fade" });
    navigationIndex = 0;
    applyRoute(navigationHistory[0].route, "fade");
  } catch (error) {
    content.innerHTML = `<div class="card"><h2>Unable to open the showcase</h2><p>${escape(error.message)}</p></div>`;
    showError(error);
  }
})();
