(() => {
  const content = document.querySelector("#rail-content");
  const rail = document.querySelector(".rail");
  if (!content || !rail) return;

  const updateBrand = () => rail.classList.toggle("rail-scrolled", content.scrollTop > 24);
  content.addEventListener("scroll", updateBrand, { passive: true });
  updateBrand();
})();
