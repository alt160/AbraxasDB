function openGroupSetupDialog(group) {
  if (group.commercialOnly && !state.commercialFeaturesAvailable) {
    openCommercialEditionNotice(group.title);
    return;
  }
  pendingCleanup = null;
  const lab = state.laboratory?.groupId === group.id ? state.laboratory : null,
    configuration = groupSetupConfiguration(group, lab);
  pendingSetup = {
    groupId: group.id,
    size: configuration.size,
    complexity: configuration.complexity,
    customerCount: configuration.primary,
    ordersPerCustomer: configuration.related,
    seed: configuration.seed,
    persistAcrossRuns: true,
  };
  const existing = state.laboratory || state.startup.presence?.hasRecordBase;
  creationBusy = false;
  creationEyebrow.textContent = "CONFIGURE GROUP SETUP";
  creationTitle.textContent = `${existing ? "Recreate" : "Prepare"} ${group.title}`;
  acceptCreationButton.textContent = existing ? "Accept and Recreate" : "Accept and Run Setup";
  creationBody.innerHTML = `<p>Choose the deterministic data shape for this group. Setup creates the RecordBase and stores first, then generates the sample records.</p>${existing ? `<div class="replacement-warning"><strong>The current Showcase RecordBase files will be permanently deleted.</strong><span>The Showcase deletes its owned files directly. It does not delete records one at a time. Cancel leaves the current files untouched.</span></div>` : ""}<section class="dialog-setup-parameters">${groupSetupFormMarkup(group, configuration)}</section><div class="setup-order"><span>1</span><strong>Create RecordBase</strong><i>then</i><span>2</span><strong>Generate sample records</strong></div><div class="progress"><i id="progress-bar"></i></div><p id="progress-label" class="progress-label">Nothing changes until you accept.</p>`;
  creationBody.querySelectorAll(".preset").forEach((button) => {
    button.onclick = () => {
      creationBody.querySelector("#dialog-primary-count").value = button.dataset.primary;
      creationBody.querySelector("#dialog-related-count").value = button.dataset.related;
      creationBody.querySelectorAll(".preset").forEach((item) => item.classList.toggle("active", item === button));
    };
  });
  creationBody.querySelectorAll("input, select").forEach((input) => {
    input.addEventListener("input", () => {
      if (input.id !== "dialog-seed" && input.id !== "dialog-complexity")
        creationBody.querySelectorAll(".preset").forEach((item) => item.classList.toggle("active", item.dataset.preset === "Custom"));
    });
  });
  acceptCreationButton.disabled = false;
  cancelCreationButton.disabled = false;
  animateCreationDialog(true);
  cancelCreationButton.focus();
}

function openGroupCleanupDialog(openGroup, destinationGroup) {
  pendingSetup = null;
  pendingCleanup = {
    groupId: openGroup.id,
    groupTitle: openGroup.title,
    destinationGroupId: destinationGroup.id,
    directory: state.laboratory?.directory || "",
  };
  creationBusy = false;
  creationEyebrow.textContent = "CONFIRM RECORDBASE CLEANUP";
  creationTitle.textContent = `Unload and delete ${openGroup.title}`;
  acceptCreationButton.textContent = "Unload and delete";
  creationBody.innerHTML = `<p>This explicit handoff closes the currently open group before the next group can be prepared.</p><div class="replacement-warning"><strong>${escape(openGroup.title)} will be unloaded and its Showcase-owned RecordBase files permanently deleted.</strong><span>Retained result objects and store references are released before file deletion. Cancel leaves the open group unchanged.</span></div><div class="cleanup-path"><span>SHOWCASE-OWNED PATH</span><strong>${escape(pendingCleanup.directory)}</strong></div>`;
  acceptCreationButton.disabled = false;
  cancelCreationButton.disabled = false;
  animateCreationDialog(true);
  cancelCreationButton.focus();
}

async function acceptCreationAction() {
  if (pendingCleanup) await acceptGroupCleanup();
  else await acceptGroupSetup();
}

function closeCreationDialog() {
  if (creationBusy) return;
  pendingSetup = null;
  pendingCleanup = null;
  footer.textContent = activeGroup()
    ? "Current group RecordBase unchanged"
    : "Setup cancelled · no group environment created";
  animateCreationDialog(false, () => (creationBody.innerHTML = ""));
}

async function acceptGroupCleanup() {
  if (creationBusy || !pendingCleanup) return;
  creationBusy = true;
  acceptCreationButton.disabled = true;
  cancelCreationButton.disabled = true;
  status.textContent = `Unloading ${pendingCleanup.groupTitle}…`;
  const destinationGroupId = pendingCleanup.destinationGroupId;
  try {
    state = await call("clear-workspace", {
      groupId: pendingCleanup.groupId,
    });
    pendingCleanup = null;
    browserState = null;
    groupSetupEditing = false;
    animateCreationDialog(false, () => (creationBody.innerHTML = ""));
    footer.textContent =
      "Prior group unloaded and its RecordBase files deleted";
    selectedGroup = destinationGroupId;
    render();
  } catch (error) {
    showError(error);
    acceptCreationButton.disabled = false;
    cancelCreationButton.disabled = false;
    try {
      state = await call("get-state");
    } catch {}
    render(false);
  } finally {
    creationBusy = false;
    status.textContent = "Ready";
  }
}
async function acceptGroupSetup() {
  if (creationBusy || !pendingSetup) return;
  pendingSetup.size = creationBody.querySelector(".preset.active")?.dataset.preset || "Custom";
  pendingSetup.complexity = creationBody.querySelector("#dialog-complexity").value;
  pendingSetup.customerCount = Number(creationBody.querySelector("#dialog-primary-count").value);
  pendingSetup.ordersPerCustomer = Number(creationBody.querySelector("#dialog-related-count").value);
  pendingSetup.seed = Number(creationBody.querySelector("#dialog-seed").value);
  creationBusy = true;
  acceptCreationButton.disabled = true;
  cancelCreationButton.disabled = true;
  status.textContent = "Replacing and preparing group RecordBase…";
  try {
    state = await call("prepare-group", pendingSetup);
    const groupId = pendingSetup.groupId;
    pendingSetup = null;
    browserState = null;
    groupSetupEditing = false;
    animateCreationDialog(false, () => (creationBody.innerHTML = ""));
    footer.textContent = "Group environment and deterministic data are ready";
    selectedGroup = groupId;
    render();
  } catch (error) {
    showError(error);
    acceptCreationButton.disabled = false;
    cancelCreationButton.disabled = false;
    try {
      state = await call("get-state");
    } catch {}
    render();
  } finally {
    creationBusy = false;
    status.textContent = "Ready";
  }
}
function animateCreationDialog(opening, after) {
  const dialog = lightbox.querySelector(".creation-dialog");
  if (opening) lightbox.hidden = false;
  const finish = () => {
    if (!opening) lightbox.hidden = true;
    after?.();
  };
  if (reducedMotion()) {
    finish();
    return;
  }
  const backdropFrames = opening
      ? [{ opacity: 0 }, { opacity: 1 }]
      : [{ opacity: 1 }, { opacity: 0 }],
    dialogFrames = opening
      ? [
          { opacity: 0, transform: "translateY(14px) scale(.98)" },
          { opacity: 1, transform: "none" },
        ]
      : [
          { opacity: 1, transform: "none" },
          { opacity: 0, transform: "translateY(10px) scale(.985)" },
        ];
  const animations = [
    lightbox.animate(backdropFrames, {
      duration: opening ? 180 : 140,
      easing: "ease-out",
    }),
    dialog.animate(dialogFrames, {
      duration: opening ? 220 : 150,
      easing: "cubic-bezier(.2,.8,.2,1)",
    }),
  ];
  Promise.allSettled(animations.map((animation) => animation.finished)).then(
    finish,
  );
}
