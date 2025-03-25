import { invariant } from '@algolia/autocomplete-shared';
export function getHTMLElement(environment, value) {
  if (typeof value === 'string') {
    var element = environment.document.querySelector(value);
    invariant(element !== null, "The element ".concat(JSON.stringify(value), " is not in the document."));
    return element;
  }

  return value;
}