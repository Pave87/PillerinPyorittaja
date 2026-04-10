window.themeInterop = {
    applyTheme: function (variables) {
        const root = document.documentElement;
        for (const key in variables) {
            if (variables.hasOwnProperty(key)) {
                root.style.setProperty(key, variables[key]);
            }
        }
    },
    clearTheme: function () {
        const root = document.documentElement;
        const style = root.style;
        const toRemove = [];
        for (let i = 0; i < style.length; i++) {
            const prop = style[i];
            if (prop.startsWith('--')) {
                toRemove.push(prop);
            }
        }
        toRemove.forEach(function (prop) {
            style.removeProperty(prop);
        });
    }
};
