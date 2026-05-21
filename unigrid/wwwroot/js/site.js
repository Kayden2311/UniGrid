// UniGrid Premium Interactive Engine & Client Scripts
document.addEventListener("DOMContentLoaded", () => {
    // 1. Reveal-on-Scroll Interaction with IntersectionObserver
    const revealElements = document.querySelectorAll(".reveal-on-scroll");
    
    if (revealElements.length > 0) {
        if ("IntersectionObserver" in window) {
            const observerOptions = {
                root: null, // Viewport
                threshold: 0.1, // Trigger when 10% visible
                rootMargin: "0px 0px -50px 0px" // Trigger slightly before it fully enters
            };
            
            const revealObserver = new IntersectionObserver((entries, observer) => {
                entries.forEach(entry => {
                    if (entry.isIntersecting) {
                        entry.target.classList.add("is-visible");
                        // Once observed and animated, we can unobserve
                        observer.unobserve(entry.target);
                    }
                });
            }, observerOptions);
            
            revealElements.forEach(el => revealObserver.observe(el));
        } else {
            // Fallback for older browsers
            revealElements.forEach(el => el.classList.add("is-visible"));
        }
    }

    // 2. Smooth Scrolling for Anchor Links
    const anchorLinks = document.querySelectorAll('a[href^="#"]');
    anchorLinks.forEach(link => {
        link.addEventListener("click", function(e) {
            const targetId = this.getAttribute("href");
            if (targetId === "#") return;
            
            const targetEl = document.querySelector(targetId);
            if (targetEl) {
                e.preventDefault();
                targetEl.scrollIntoView({
                    behavior: "smooth",
                    block: "start"
                });
            }
        });
    });

    // 3. Dynamic Tooltip Helper for calendar or tasks
    window.createTooltip = function(el, text) {
        let tooltip = document.createElement("div");
        tooltip.className = "absolute z-50 px-3 py-1.5 text-xs font-bold text-white bg-slate-900/90 backdrop-blur-md rounded-lg shadow-lg border border-slate-800 pointer-events-none transition-all opacity-0 scale-95 transform -translate-y-2";
        tooltip.innerText = text;
        document.body.appendChild(tooltip);

        const rect = el.getBoundingClientRect();
        const tooltipRect = tooltip.getBoundingClientRect();
        
        tooltip.style.left = `${rect.left + (rect.width / 2) - (tooltipRect.width / 2) + window.scrollX}px`;
        tooltip.style.top = `${rect.top - tooltipRect.height - 8 + window.scrollY}px`;
        
        // Trigger show animation frame
        requestAnimationFrame(() => {
            tooltip.classList.remove("opacity-0", "scale-95", "-translate-y-2");
            tooltip.classList.add("opacity-100", "scale-100", "translate-y-0");
        });

        // Hide and remove helper
        el.addEventListener("mouseleave", () => {
            tooltip.classList.remove("opacity-100", "scale-100", "translate-y-0");
            tooltip.classList.add("opacity-0", "scale-95", "-translate-y-2");
            setTimeout(() => tooltip.remove(), 200);
        }, { once: true });
    };

    // 4. Lucide Dynamic Re-Initialization
    window.reinitLucide = function() {
        if (typeof lucide !== "undefined") {
            lucide.createIcons();
        }
    };
});
