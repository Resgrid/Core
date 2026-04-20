# Apps

This package builds the Razor-hosted React custom elements used by `Resgrid.Web`.

## Components

- `rg-map`
- `rg-shifts-calendar`
- `rg-omnibar`

Each component is exposed as a custom element so Razor pages can render it directly and pass values through HTML attributes or DOM properties.

## Development

Run `npm install` to restore dependencies.

Run `npm run watch` to rebuild the element bundles on changes.

## Build

Run `npm run build` to produce the production-ready assets in `dist\core`.

`Resgrid.Web.csproj` copies those files into `wwwroot\js\ng`, where the Razor layout loads them as ES modules.
