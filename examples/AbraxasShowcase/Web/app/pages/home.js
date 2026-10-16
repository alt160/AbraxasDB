function renderHome() {
  const saved = state.startup.presence?.hasRecordBase;
  content.innerHTML = `<div class="welcome-page"><section class="welcome-hero"><div><p class="eyebrow">LEARN ABRAXAS BY RUNNING IT</p><h2>A practical primer and executable reference</h2><p>Know SQL tables, Mongo collections, or key-value stores? Start with familiar jobs—save an object, find it again, and read related data—then see how Abraxas handles each one. The examples show paste-ready C#; most groups let you configure their sample data.</p><div class="welcome-actions"><button id="welcome-catalog" class="primary" type="button">Browse scenario groups</button></div></div><div class="welcome-mark"><img class="welcome-logo" src="assets/AbraxasDB.png" alt="Abraxas"></div></section><section class="welcome-grid"><article><p class="eyebrow">WHAT YOU CAN DO</p><h3>Read, run, and inspect</h3><p>Copy the Abraxas code into a console app, run the same operation here, and expand the real .NET result.</p></article><article><p class="eyebrow">CONTROL THE DATA</p><h3>Compare different sizes</h3><p>Choose the record count, related-record count, and random seed. Recreate the group whenever you want a clean starting point.</p></article><article><p class="eyebrow">SAFE BOUNDARY</p><h3>Built for learning</h3><p>The Showcase opens only its own example files. It is not a general RecordBase administration tool and will not open your application data.</p></article></section><section class="home-state"><div><small>SHOWCASE RECORDBASE</small><strong>${saved ? "Saved example files found · closed" : "No saved example files found"}</strong><span>${saved ? "Nothing was opened at startup. Open the matching scenario group when you want to use those files." : "Choose a scenario group to create its RecordBase and sample data."}</span></div><button id="home-first-group" class="secondary" type="button">View scenario groups</button></section></div>`;
  document.querySelector("#welcome-catalog").onclick = () => openCatalog();
  document.querySelector("#home-first-group").onclick = () => openCatalog();
}
async function openExistingWorkspace(groupId) {
  status.textContent = "Opening saved RecordBase…";
  try {
    state = await call("choose-workspace", { action: "open", groupId });
    groupSetupEditing = false;
    footer.textContent = "Saved RecordBase opened by explicit request";
    selectedGroup = groupId;
    render();
  } catch (error) {
    showError(error);
  } finally {
    status.textContent = "Ready";
  }
}

