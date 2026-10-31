(function () {
        // Update Document Title
        document.title = "GM FileStorage Sample";

        // Replace Icon
        var link = document.querySelector("link[rel*='icon']") || document.createElement("link");
        link.type = "image/x-icon";
        link.rel = "shortcut icon";
        link.href = "Assets/favicon.ico";
        document.getElementsByTagName("head")[0].appendChild(link);
})();