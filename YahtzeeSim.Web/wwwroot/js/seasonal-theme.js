(function () {
    // Change enabled to false to return the whole app to its standard theme.
    const config = Object.freeze({
        enabled: true,
        theme: 'christmas',
        snow: true,
        elves: true
    });

    const definitions = {
        christmas: {
            color: '#0b493c',
            manifest: 'manifest.christmas.webmanifest',
            penColors: ['#0B493C', '#B51F2E', '#17634F', '#245B78', '#6D2E46', '#91621A']
        }
    };

    const active = config.enabled ? definitions[config.theme] : null;
    if (active) {
        document.documentElement.dataset.seasonalTheme = config.theme;
    }

    applyPwaTheme(active);
    window.yahtzeeSimSeasonalTheme = Object.freeze({
        suggestPenColor: function () {
            const colors = active && active.penColors;
            return colors ? colors[Math.floor(Math.random() * colors.length)] : null;
        }
    });
    window.addEventListener('pageshow', () => applyPwaTheme(active));
    window.addEventListener('visibilitychange', () => {
        if (!document.hidden) {
            applyPwaTheme(active);
        }
    });

    if (active && (config.snow || config.elves)) {
        if (document.readyState === 'loading') {
            window.addEventListener('DOMContentLoaded', addEffects, { once: true });
        } else {
            addEffects();
        }
    }

    function applyPwaTheme(definition) {
        const color = definition ? definition.color : '#1f6f5f';
        const themeMeta = document.getElementById('app-theme-color');
        if (themeMeta) {
            themeMeta.content = color;
        }

        const manifest = document.getElementById('app-manifest');
        if (manifest) {
            manifest.href = definition ? definition.manifest : 'manifest.webmanifest';
        }
    }

    function addEffects() {
        if (document.getElementById('seasonal-effects')) {
            return;
        }

        const seed = dailySeed();
        const random = seededRandom(seed);
        const effects = document.createElement('div');
        effects.id = 'seasonal-effects';
        effects.setAttribute('aria-hidden', 'true');

        if (config.snow) {
            const snow = document.createElement('div');
            snow.className = 'seasonal-snow';
            for (let index = 0; index < 18; index++) {
                const flake = document.createElement('i');
                flake.className = 'seasonal-snowflake';
                flake.textContent = '\u2744';
                flake.style.setProperty('--snow-x', `${Math.round(random() * 100)}vw`);
                flake.style.setProperty('--snow-size', `${(0.45 + random() * 0.55).toFixed(2)}rem`);
                flake.style.setProperty('--snow-duration', `${(11 + random() * 12).toFixed(1)}s`);
                flake.style.setProperty('--snow-delay', `${(-random() * 18).toFixed(1)}s`);
                flake.style.setProperty('--snow-drift', `${Math.round(random() * 80 - 40)}px`);
                snow.appendChild(flake);
            }
            effects.appendChild(snow);
        }

        if (config.elves) {
            const elf = document.createElement('div');
            const side = random() > 0.5 ? 'seasonal-elf-right' : 'seasonal-elf-left';
            const hat = random() > 0.5 ? 'seasonal-elf-green' : 'seasonal-elf-red';
            elf.className = `seasonal-elf ${side} ${hat}`;
            elf.innerHTML = [
                '<span class="seasonal-elf-hat"></span>',
                '<span class="seasonal-elf-ear seasonal-elf-ear-left"></span>',
                '<span class="seasonal-elf-ear seasonal-elf-ear-right"></span>',
                '<span class="seasonal-elf-face"><i></i></span>',
                '<span class="seasonal-elf-body"></span>',
                '<span class="seasonal-elf-die">&#9860;</span>'
            ].join('');
            effects.appendChild(elf);
            window.setTimeout(() => elf.remove(), 15000);
        }

        document.body.appendChild(effects);
    }

    function dailySeed() {
        const now = new Date();
        return Number(`${now.getFullYear()}${String(now.getMonth() + 1).padStart(2, '0')}${String(now.getDate()).padStart(2, '0')}`);
    }

    function seededRandom(seed) {
        let state = seed >>> 0;
        return function () {
            state = (state * 1664525 + 1013904223) >>> 0;
            return state / 4294967296;
        };
    }
})();
