namespace AndreasBehrend.NINA.Phd2Api.WebApi {

    internal static class SwaggerUi {

        public static string GetHtml(int port) => $@"<!DOCTYPE html>
<html lang=""en"">
<head>
  <meta charset=""UTF-8"">
  <meta name=""viewport"" content=""width=device-width, initial-scale=1.0"">
  <title>PHD2 API</title>
  <link rel=""stylesheet"" href=""https://unpkg.com/swagger-ui-dist@5/swagger-ui.css"">
  <style>
    html {{ box-sizing: border-box; overflow-y: scroll; }}
    *, *:before, *:after {{ box-sizing: inherit; }}
    body {{ margin: 0; padding: 0; background: #fafafa; }}
    .swagger-ui .topbar {{ background-color: #1b3a5c; }}
    .swagger-ui .topbar .download-url-wrapper {{ display: none; }}
  </style>
</head>
<body>
  <div id=""swagger-ui""></div>
  <script src=""https://unpkg.com/swagger-ui-dist@5/swagger-ui-bundle.js""></script>
  <script src=""https://unpkg.com/swagger-ui-dist@5/swagger-ui-standalone-preset.js""></script>
  <script>
    window.onload = function() {{
      SwaggerUIBundle({{
        url: window.location.protocol + '//' + window.location.host + '/api/v1/openapi.json',
        dom_id: '#swagger-ui',
        deepLinking: true,
        presets: [SwaggerUIBundle.presets.apis, SwaggerUIStandalonePreset],
        plugins: [SwaggerUIBundle.plugins.DownloadUrl],
        layout: 'StandaloneLayout',
        defaultModelsExpandDepth: 2,
        defaultModelExpandDepth: 2,
        displayRequestDuration: true,
        tryItOutEnabled: true,
        filter: true
      }});
    }};
  </script>
</body>
</html>";
    }
}
