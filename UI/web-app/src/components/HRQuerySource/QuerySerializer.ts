import { Group } from "../../models/Group";
import { IFilterPart } from "../../models/IFilterPart";

export function stringifyGroup(group: Group, isChild?: boolean, childIndex?: number, childrenLength?: number): string {

    let result = '(';
    result += `${group.items.map((item, index) => {
      if (index < group.items.length - 1) {
          return item.attribute + ' ' + item.equalityOperator + ' ' + item.value + ' ' + item.andOr;
      } else {
          return item.attribute + ' ' + item.equalityOperator + ' ' + item.value;
      }
    }).join(' ')}`;

    if (group.children.length === 0) {
      result += ')';
    }

    if (isChild) {
      if (childrenLength && childIndex !== childrenLength-1) result += ` ${group.andOr} `;
    }
    else {
      result += ` ${group.andOr} `;
    }

    group.children.forEach((child, index) => {
        result += stringifyGroup(child, true, index, group.children.length);
        if (index < group.children.length - 1) {
            result += ' ';
        }
    });

    if (group.children.length > 0) {
      result += ')';
      result += ` ${group.children[group.children.length-1].andOr} `;
    }

    return result;
}

export function stringifyGroups(groups: Group[]): string {
    let result = '';

    groups.forEach((group, index) => {
        result += stringifyGroup(group);
        if (index < groups.length - 1) {
            result += ' ';
        }
    });

    return result;
}

function parseFilterPart(part: string): IFilterPart {
  const operators = ["<=", ">=", "<>", "=", ">", "<"];
  let operatorFound = '';
  let operatorIndex = -1;

  for (const operator of operators) {
    const index = part.indexOf(operator);
    if (index !== -1) {
      operatorFound = operator;
      operatorIndex = index;
      break;
    }
  }

  if (operatorIndex === -1) {
    return {
      attribute : "",
      equalityOperator: "invalid",
      value: "",
      andOr: ""
    };
  }
  const attribute = part.slice(0, operatorIndex).trim();
  const value = part.slice(operatorIndex + operatorFound.length).trim();

  return {
    attribute,
    equalityOperator: operatorFound,
    value,
    andOr: ""
  };
}

function findPartsOfString(string: string, substringArray: { currentSegment: string, start: number; end: number }[]): { currentSegment: string, start: number; end: number, andOr: string }[] {
  const output: { currentSegment: string, start: number; end: number, andOr: "" }[] = [];
  let lastEnd = 0;

  for (const substringInfo of substringArray) {
      const { currentSegment, start, end } = substringInfo;
      if (start > lastEnd) {
        output.push({
            currentSegment: string.substring(lastEnd, start),
            start: lastEnd,
            end: start - 1,
            andOr: ""
        });
      }
      output.push({ currentSegment, start, end, andOr: "" });
      lastEnd = end + 1;
  }
  if (lastEnd < string.length) {
      output.push({
          currentSegment: string.substring(lastEnd),
          start: lastEnd,
          end: string.length - 1,
          andOr: ""
      });
  }
  return output;
}

function appendAndOr(allParts: { currentSegment: string; start: number; end: number; andOr: string; }[]) {
  allParts.forEach((segment, index) => {
    let modifiedSegment = segment.currentSegment.trim();
    let startWord = '';
    let endWord = '';

    const lowerCaseSegment = modifiedSegment.toLowerCase();

    if (lowerCaseSegment.startsWith('and ')) {
        startWord = 'And';
        modifiedSegment = modifiedSegment.substring(4).trim();
    } else if (lowerCaseSegment.startsWith('or ')) {
        startWord = 'Or';
        modifiedSegment = modifiedSegment.substring(3).trim();
    }

    if (lowerCaseSegment.endsWith(' and')) {
        endWord = 'And';
        modifiedSegment = modifiedSegment.substring(0, modifiedSegment.length - 4).trim();
    } else if (lowerCaseSegment.endsWith(' or')) {
        endWord = 'Or';
        modifiedSegment = modifiedSegment.substring(0, modifiedSegment.length - 3).trim();
    }

    if (lowerCaseSegment === 'and') {
      startWord = 'And';
      modifiedSegment = '';
    } else if (lowerCaseSegment === 'or') {
      startWord = 'Or';
      modifiedSegment = '';
    }

    if (startWord !== '') {
      allParts[index-1].andOr = startWord;
    }
    if (endWord !== '') {
      allParts[index].andOr = endWord;
    }

    allParts[index].currentSegment = modifiedSegment;

    if (modifiedSegment === '') {
      allParts.splice(index, 1);
    } else {
      allParts[index].currentSegment = modifiedSegment;
    }
  });
  return allParts;
}

export function parseGroup(input: string): Group[] {
  const groups: Group[] = [];
  let subStrings: { currentSegment: string, start: number; end: number}[] = [];
  let depth = 0;
  let currentSegment = '';
  let operators: string[] = [];

  input = input.trim();
  let start: number = 0;
  let end: number = input.length - 1;

  for (let i = 0; i < input.length; i++) {
    const char = input[i];

    if (char === '(') {
        if (depth > 0) {
            currentSegment += char;
        }
        depth++;
        if (depth === 1) start = i;
    } else if (char === ')') {
        depth--;
        if (depth === 0) {
            end = i;
            subStrings.push({ currentSegment, start, end});
            currentSegment = '';
        } else {
            currentSegment += char;
        }
    } else if (depth === 0 && (input.substr(i, 3) === ' Or' || input.substr(i, 4) === ' And')) {
        operators.push(input.substr(i, input.substr(i, 4) === ' And' ? 4 : 3).trim());
        i += operators[operators.length - 1].length - 1;
    } else if (depth > 0) {
        currentSegment += char;
    }
  }

  var allParts = findPartsOfString(input, subStrings);
  var allPartsWithAndOr = appendAndOr(allParts);
  let invalid = false;

  allPartsWithAndOr.forEach((currentSegment, i) => {
    var result = parseSegment(currentSegment.currentSegment);
    if ((result.name === "invalid") || (result.children.length > 0 && result.children.some(childItem => childItem.name === "invalid"))) {
      invalid = true;
    }
    else {
      groups.push(result);
      if (groups[i] && groups[i].children && groups[i].children.length > 0 && groups[i].children[groups[i].children.length-1] && groups[i].children[groups[i].children.length-1].andOr !== null) {
        groups[i].children[groups[i].children.length-1].andOr = allPartsWithAndOr[i].andOr;
      }
      else if (allPartsWithAndOr[i].andOr !== '') groups[i].andOr = allPartsWithAndOr[i].andOr;
    }
  });
  return invalid ? [] : groups;
}

function parseSegment(segment: string, groupOperator?: string): Group {
  if (segment.includes('(') && segment.includes(')')) {
    let children: Group[] = [];
      const innerSegments = segment.match(/\((.*?)\)/g)?.map(innerSegment => innerSegment.replace(/^\(|\)$/g, ''));
      const contentOutsideParentheses = segment.replace(/\s*\([^)]*\)\s*/g, '||').split('||');
        if (innerSegments) {
          innerSegments.forEach((innerSegment, index) => {
            const childGroup = parseSegment(innerSegment, contentOutsideParentheses && contentOutsideParentheses.length >= 0 ? contentOutsideParentheses[index+1] : "");
            children.push(childGroup);
          });
        }

        let start = segment.indexOf('(');
        let end = segment.lastIndexOf(')');
        let remainingSegment = segment.substring(0, start) + segment.substring(end + 1);
        var matchOperator  = remainingSegment.match(/^\s*(Or|And)|\s*(Or|And)\s*$/gi);
        var operator = matchOperator ? matchOperator[0].trim() : null;
        remainingSegment = remainingSegment.replace(/^\s*(Or|And)|\s*(Or|And)\s*$/gi, '').trim();
        if (remainingSegment) {
          return {
              name: '',
              items: parseSegment(remainingSegment).items,
              children: children,
              andOr: operator ?? ''
          };
      }
  }
  const items = segment.split(/ And | Or /gi).map(parseFilterPart);
  if (items.some(item => item.equalityOperator === "invalid")) {
    return {
      name: 'invalid',
      items : [],
      children: [],
      andOr: ''
    };
  }
  else {
    const operators = segment.match(/(?: And | Or )/gi) || [];
    items.forEach((item, index) => {
        if (index < items.length - 1) {
            item.andOr = operators[index].trim();
        }
    });
    return {
        name: '',
        items,
        children: [],
        andOr: groupOperator ?? ''
    };
  }
}