# Test

This project was generated using [Angular CLI](https://github.com/angular/angular-cli) version 22.1.5. See `package.json` for the exact pinned version.

## Development server

To start a local development server, run:

```bash
ng serve
```

Once the server is running, open your browser and navigate to `http://localhost:57729/`. The application will automatically reload whenever you modify any of the source files.

## Code scaffolding

Angular CLI includes powerful code scaffolding tools. To generate a new component, run:

```bash
ng generate component component-name
```

For a complete list of available schematics (such as `components`, `directives`, or `pipes`), run:

```bash
ng generate --help
```

## Building

To build the project run:

```bash
ng build
```

This will compile your project and store the build artifacts in the `dist/` directory. By default, the production build optimizes your application for performance and speed.

## Running unit tests

To execute unit tests with the [Vitest](https://vitest.dev/) test runner, use the following command:

```bash
ng test
```

## Running end-to-end tests

For end-to-end (e2e) testing, run:

```bash
ng e2e
```

Angular CLI does not come with an end-to-end testing framework by default. You can choose one that suits your needs.

## Additional Resources

For more information on using the Angular CLI, including detailed command references, visit the [Angular CLI Overview and Command Reference](https://angular.dev/tools/cli) page.

## How to add template to VS2022:
## to uninstall existing template
dotnet new uninstall subtitletranslator
          
## to install new template to a folder of a project - make sure you to type '.' 
          
dotnet new install .
          
## to clone the template to a new project
          
dotnet new standalone-angular -o <your NewProject>

## cannot run "npm start"
If `npm start` fails right after creating a new project from this template, `node_modules`
sometimes ends up incomplete. Fix with a clean reinstall:

```
rmdir /s /q node_modules
npm install
ng build
```
