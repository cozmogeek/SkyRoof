# Code Style

### General Guidelines
- Never implement functionality that was not explicitly requested. For example: if asked to add a "Save" button, add the button and any minimal wiring (event hookup) but do not implement the file-saving logic unless explicitly requested to do so.
- Never remove any comments or blank lines
- Ensure that no syntax errors are introduced by your formatting changes

### Code Style Guidelines
- Indent with 2 spaces
- Use camelCase for variables and parameters
- Use PascalCase for all other identifiers
- Never start or end identifiers with an underscore (_)
- Do not put curly braces around a single statement if syntax rules allow
- Place the statement that follows if(), while(), etc. on the same line if it fits
- Start a comment with a lower case letter unless it is all-capitals

### Grouping and Ordering
- Group related functions together and order them in a logical way (e.g. public methods before private methods, event handlers together, etc.). If function A calls function B, then function B should be defined after function A.
- Separate groups of functions with a 3-line comment header, where the first and third lines are strings of dashes (---) 100-characters long, and the middle line is a comment describing the group (e.g. "mouse events") centered within the dashes. Add 4 blank lines before the header, and no blank lines after the header. For example:

```cs
// ----------------------------------------------------------------------------------------------------
//                                            mouse events
// ----------------------------------------------------------------------------------------------------
```
