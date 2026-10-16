function openConceptDetails(concept) {
  if (!concept?.details?.length) return;
  conceptTitle.textContent = concept.term;
  const fileSet = concept.sharedFileBaseName
    ? `<section class="recordbase-file-map" aria-label="Files sharing the ${escape(concept.sharedFileBaseName)} RecordBase name"><div class="file-map-stem"><small>SHARED RECORDBASE NAME</small><strong>${escape(concept.sharedFileBaseName)}</strong></div><div class="file-map-branches">${concept.details
        .filter((detail) => detail.title.startsWith("."))
        .map(
          (detail) =>
            `<div><span>${escape(concept.sharedFileBaseName)}</span><strong>${escape(detail.title)}</strong></div>`,
        )
        .join(
          "",
        )}</div><p>Abraxas opens these sibling files together by directory and shared RecordBase name.</p></section>`
    : "";
  conceptBody.innerHTML = `<p class="concept-introduction">${escape(concept.detailIntroduction)}</p>${fileSet}<div class="concept-detail-list">${concept.details.map((detail) => `<article><small>${detail.title.startsWith(".") ? "FILE EXTENSION" : ""}</small><strong>${escape(detail.title)}</strong>${concept.sharedFileBaseName && detail.title.startsWith(".") ? `<code>${escape(concept.sharedFileBaseName + detail.title)}</code>` : ""}<p>${escape(detail.description)}</p></article>`).join("")}</div>`;
  animateConceptDialog(true);
  conceptCloseIcon.focus();
}
function closeConceptDetails() {
  animateConceptDialog(false, () => {
    conceptTitle.textContent = "";
    conceptBody.innerHTML = "";
  });
}
function animateConceptDialog(opening, after) {
  const dialog = conceptLightbox.querySelector(".concept-dialog");
  if (opening) conceptLightbox.hidden = false;
  const finish = () => {
    if (!opening) conceptLightbox.hidden = true;
    after?.();
  };
  if (reducedMotion()) {
    finish();
    return;
  }
  const animations = [
    conceptLightbox
      .querySelector(".concept-backdrop")
      .animate(
        opening
          ? [{ opacity: 0 }, { opacity: 1 }]
          : [{ opacity: 1 }, { opacity: 0 }],
        { duration: opening ? 180 : 140, easing: "ease-out" },
      ),
    dialog.animate(
      opening
        ? [
            { opacity: 0, transform: "translateY(16px) scale(.98)" },
            { opacity: 1, transform: "none" },
          ]
        : [
            { opacity: 1, transform: "none" },
            { opacity: 0, transform: "translateY(10px) scale(.985)" },
          ],
      { duration: opening ? 230 : 150, easing: "cubic-bezier(.2,.8,.2,1)" },
    ),
  ];
  Promise.allSettled(animations.map((animation) => animation.finished)).then(
    finish,
  );
}
