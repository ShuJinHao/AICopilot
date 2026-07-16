import fs from "node:fs";
import path from "node:path";
import { createRequire } from "node:module";
import { fileURLToPath } from "node:url";

const [repositoryArgument, outputArgument] = process.argv.slice(2);
if (!repositoryArgument || !outputArgument) {
  process.stderr.write("Usage: AICopilot.TypeScriptCompatibilityProbe <repository-root> <output-json>\n");
  process.exit(2);
}

const repositoryRoot = path.resolve(repositoryArgument);
const outputPath = path.resolve(outputArgument);
const require = createRequire(import.meta.url);
const toolRoot = path.dirname(fileURLToPath(import.meta.url));
const webRoot = path.join(repositoryRoot, "src/vues/AICopilot.Web");
const dependencyWebRoot = path.resolve(toolRoot, "../../../src/vues/AICopilot.Web");
const ts = require(path.join(dependencyWebRoot, "node_modules/typescript/lib/typescript.js"));
const { parse: parseVue } = require(path.join(dependencyWebRoot, "node_modules/@vue/compiler-sfc"));
const { baseParse: parseVueTemplate, NodeTypes: VueNodeTypes } = require(
  path.join(dependencyWebRoot, "node_modules/@vue/compiler-dom"));
const signalPattern = /(?:Alias|Adapter|Wrapper|Fallback|Compatibility|Legacy|Shadow|DualWrite|Obsolete)/i;
const templateIdentifierCounts = new Map();

const sourceRoot = path.join(webRoot, "src");
const sourceEntries = [];
if (fs.existsSync(sourceRoot)) {
  for (const filePath of enumerateFiles(sourceRoot)) {
    if (filePath.endsWith(".ts")) {
      sourceEntries.push({
        virtualPath: filePath,
        originalPath: filePath,
        text: fs.readFileSync(filePath, "utf8"),
      });
    } else if (filePath.endsWith(".vue")) {
      const source = fs.readFileSync(filePath, "utf8");
      const descriptor = parseVue(source, { filename: filePath }).descriptor;
      if (descriptor.template) {
        collectTemplateIdentifiers(descriptor.template.content, templateIdentifierCounts);
      }
      const blocks = [descriptor.script, descriptor.scriptSetup].filter(Boolean);
      if (blocks.length === 0) {
        continue;
      }
      const text = blocks
        .map((block) => "\n".repeat(Math.max(0, block.loc.start.line - 1)) + block.content)
        .join("\n");
      sourceEntries.push({
        virtualPath: `${filePath}.ts`,
        originalPath: filePath,
        text,
      });
    }
  }
}

const entryByVirtualPath = new Map(sourceEntries.map((entry) => [normalize(entry.virtualPath), entry]));
const configPath = path.join(webRoot, "tsconfig.json");
let compilerOptions = {
  target: ts.ScriptTarget.ESNext,
  module: ts.ModuleKind.ESNext,
  moduleResolution: ts.ModuleResolutionKind.Bundler,
  skipLibCheck: true,
};
if (fs.existsSync(configPath)) {
  const config = ts.readConfigFile(configPath, ts.sys.readFile);
  if (!config.error) {
    compilerOptions = ts.parseJsonConfigFileContent(config.config, ts.sys, webRoot).options;
  }
}

const defaultHost = ts.createCompilerHost(compilerOptions, true);
const host = {
  ...defaultHost,
  fileExists(fileName) {
    return entryByVirtualPath.has(normalize(fileName)) || defaultHost.fileExists(fileName);
  },
  readFile(fileName) {
    return entryByVirtualPath.get(normalize(fileName))?.text ?? defaultHost.readFile(fileName);
  },
  getSourceFile(fileName, languageVersion, onError, shouldCreateNewSourceFile) {
    const entry = entryByVirtualPath.get(normalize(fileName));
    if (entry) {
      return ts.createSourceFile(fileName, entry.text, languageVersion, true, ts.ScriptKind.TS);
    }
    return defaultHost.getSourceFile(fileName, languageVersion, onError, shouldCreateNewSourceFile);
  },
};
const program = ts.createProgram({
  rootNames: sourceEntries.map((entry) => entry.virtualPath),
  options: compilerOptions,
  host,
});
const checker = program.getTypeChecker();
const programSources = program.getSourceFiles().filter((sourceFile) =>
  entryByVirtualPath.has(normalize(sourceFile.fileName)));
const declarations = [];

for (const sourceFile of programSources) {
  visit(sourceFile, (node) => {
    const nameNode = getDeclarationName(node);
    if (!nameNode) {
      return;
    }
    const name = getNameText(nameNode);
    if (!name || !signalPattern.test(name)) {
      return;
    }
    if (isVueI18nCompositionModeOption(node, name)) {
      return;
    }
    const symbol = resolveSymbol(checker.getSymbolAtLocation(nameNode));
    if (!symbol) {
      return;
    }
    declarations.push({ sourceFile, node, nameNode, name, symbol });
  });
}

const signals = declarations.map((declaration) => {
  const references = [];
  for (const sourceFile of programSources) {
    visit(sourceFile, (node) => {
      if (!isReferenceName(node) || node === declaration.nameNode) {
        return;
      }
      if (!symbolsEquivalent(checker.getSymbolAtLocation(node), declaration.symbol) ||
          isTypeOnlyReference(node) ||
          isMetadataOnlyReference(node) ||
          isWriteOnlyReference(node)) {
        return;
      }
      if (isCallableDeclaration(declaration.node) && !isInvokedReference(node)) {
        return;
      }
      if ((isTypeDeclaration(declaration.node) || isCallableDeclaration(declaration.node)) &&
          sourceFile === declaration.sourceFile &&
          node.pos >= declaration.node.pos &&
          node.end <= declaration.node.end) {
        return;
      }
      references.push(`${sourceFile.fileName}:${node.pos}`);
    });
  }

  if (ts.isPropertyAssignment(declaration.node) &&
      !isCallableDeclaration(declaration.node) &&
      objectLiteralHasRuntimeConsumer(declaration.node.parent)) {
    references.push(`${declaration.sourceFile.fileName}:${declaration.node.pos}:object-consumer`);
  }
  if (ts.isPropertySignature(declaration.node) || ts.isPropertyDeclaration(declaration.node)) {
    for (let index = 0; index < (templateIdentifierCounts.get(declaration.name) ?? 0); index += 1) {
      references.push(`vue-template:${declaration.name}:${index}`);
    }
    if (ts.isPropertySignature(declaration.node)) {
      for (const sourceFile of programSources) {
        visit(sourceFile, (node) => {
          if (!ts.isPropertyAssignment(node) || getNameText(node.name) !== declaration.name) {
            return;
          }
          const contextualType = checker.getContextualType(node.parent);
          const contextualProperty = resolveSymbol(contextualType?.getProperty(declaration.name));
          if (symbolsEquivalent(contextualProperty, declaration.symbol) &&
              objectLiteralHasRuntimeConsumer(node.parent)) {
            references.push(`${sourceFile.fileName}:${node.pos}:contextual-property`);
          }
        });
      }
    }
  }

  const entry = entryByVirtualPath.get(normalize(declaration.sourceFile.fileName));
  const line = declaration.sourceFile.getLineAndCharacterOfPosition(declaration.node.getStart()).line + 1;
  return {
    path: path.relative(repositoryRoot, entry.originalPath).replaceAll(path.sep, "/"),
    line,
    text: declaration.node.getText(declaration.sourceFile).replace(/\s+/g, " ").trim(),
    name: declaration.name,
    referenceCount: new Set(references).size,
  };
});

signals.sort((left, right) =>
  left.path.localeCompare(right.path) || left.line - right.line || left.name.localeCompare(right.name));
fs.mkdirSync(path.dirname(outputPath), { recursive: true });
fs.writeFileSync(outputPath, JSON.stringify({ signals }, null, 2));

function* enumerateFiles(root) {
  for (const entry of fs.readdirSync(root, { withFileTypes: true })) {
    if (["node_modules", "dist", "artifacts", "generated", "fixtures"].includes(entry.name)) {
      continue;
    }
    const fullPath = path.join(root, entry.name);
    if (entry.isDirectory()) {
      yield* enumerateFiles(fullPath);
    } else if (entry.isFile() && (fullPath.endsWith(".ts") || fullPath.endsWith(".vue"))) {
      yield fullPath;
    }
  }
}

function normalize(filePath) {
  return path.resolve(filePath).replaceAll("\\", "/");
}

function visit(node, callback) {
  callback(node);
  node.forEachChild((child) => visit(child, callback));
}

function getDeclarationName(node) {
  if (ts.isVariableDeclaration(node) ||
      ts.isFunctionDeclaration(node) ||
      ts.isClassDeclaration(node) ||
      ts.isInterfaceDeclaration(node) ||
      ts.isTypeAliasDeclaration(node) ||
      ts.isEnumDeclaration(node) ||
      ts.isMethodDeclaration(node) ||
      ts.isMethodSignature(node) ||
      ts.isPropertyDeclaration(node) ||
      ts.isPropertySignature(node) ||
      ts.isGetAccessorDeclaration(node) ||
      ts.isSetAccessorDeclaration(node) ||
      ts.isPropertyAssignment(node)) {
    return node.name && isReferenceName(node.name) ? node.name : undefined;
  }
  return undefined;
}

function getNameText(node) {
  return ts.isIdentifier(node) || ts.isPrivateIdentifier(node) || ts.isStringLiteralLike(node)
    ? node.text
    : undefined;
}

function isVueI18nCompositionModeOption(node, name) {
  if (name !== "legacy" ||
      !ts.isPropertyAssignment(node) ||
      node.initializer.kind !== ts.SyntaxKind.FalseKeyword ||
      !ts.isObjectLiteralExpression(node.parent)) {
    return false;
  }

  const call = node.parent.parent;
  if (!ts.isCallExpression(call) || call.arguments[0] !== node.parent || !ts.isIdentifier(call.expression)) {
    return false;
  }

  const importSymbol = checker.getSymbolAtLocation(call.expression);
  return (importSymbol?.declarations ?? []).some((declaration) => {
    if (!ts.isImportSpecifier(declaration)) {
      return false;
    }
    const importedName = declaration.propertyName?.text ?? declaration.name.text;
    const importDeclaration = declaration.parent?.parent?.parent;
    return importedName === "createI18n" &&
      ts.isImportDeclaration(importDeclaration) &&
      ts.isStringLiteral(importDeclaration.moduleSpecifier) &&
      importDeclaration.moduleSpecifier.text === "vue-i18n";
  });
}

function isReferenceName(node) {
  return ts.isIdentifier(node) || ts.isPrivateIdentifier(node) || ts.isStringLiteralLike(node);
}

function resolveSymbol(symbol) {
  if (symbol && (symbol.flags & ts.SymbolFlags.Alias) !== 0) {
    return checker.getAliasedSymbol(symbol);
  }
  return symbol;
}

function symbolsEquivalent(left, right) {
  const resolvedLeft = resolveSymbol(left);
  const resolvedRight = resolveSymbol(right);
  if (!resolvedLeft || !resolvedRight) {
    return false;
  }
  if (resolvedLeft === resolvedRight) {
    return true;
  }
  const leftDeclarations = resolvedLeft.getDeclarations?.() ?? resolvedLeft.declarations ?? [];
  const rightDeclarations = resolvedRight.getDeclarations?.() ?? resolvedRight.declarations ?? [];
  return leftDeclarations.some((leftDeclaration) =>
    rightDeclarations.some((rightDeclaration) =>
      leftDeclaration === rightDeclaration ||
      (leftDeclaration.pos === rightDeclaration.pos &&
       leftDeclaration.end === rightDeclaration.end &&
       normalize(leftDeclaration.getSourceFile().fileName) ===
         normalize(rightDeclaration.getSourceFile().fileName))));
}

function isTypeDeclaration(node) {
  return ts.isClassDeclaration(node) ||
    ts.isInterfaceDeclaration(node) ||
    ts.isTypeAliasDeclaration(node) ||
    ts.isEnumDeclaration(node);
}

function isCallableDeclaration(node) {
  return ts.isFunctionDeclaration(node) ||
    ts.isMethodDeclaration(node) ||
    ts.isMethodSignature(node) ||
    ts.isGetAccessorDeclaration(node) ||
    ts.isSetAccessorDeclaration(node) ||
    ((ts.isVariableDeclaration(node) ||
      ts.isPropertyDeclaration(node) ||
      ts.isPropertyAssignment(node)) &&
      node.initializer &&
      (ts.isArrowFunction(node.initializer) || ts.isFunctionExpression(node.initializer)));
}

function isTypeOnlyReference(node) {
  for (let current = node.parent; current; current = current.parent) {
    if (ts.isTypeNode(current) || ts.isInterfaceDeclaration(current) || ts.isTypeAliasDeclaration(current)) {
      return true;
    }
    if (ts.isExpression(current) || ts.isStatement(current)) {
      return false;
    }
  }
  return false;
}

function isWriteOnlyReference(node) {
  const expression = ts.isPropertyAccessExpression(node.parent) && node.parent.name === node
    ? node.parent
    : node;
  const parent = expression.parent;
  return ts.isBinaryExpression(parent) &&
    parent.left === expression &&
    parent.operatorToken.kind === ts.SyntaxKind.EqualsToken;
}

function isMetadataOnlyReference(node) {
  for (let current = node; current?.parent; current = current.parent) {
    if (ts.isTypeOfExpression(current.parent) && current.parent.expression === current) {
      return true;
    }
    if (ts.isStatement(current.parent) || ts.isDeclaration(current.parent)) {
      return false;
    }
  }
  return false;
}

function isInvokedReference(node) {
  const expression = ts.isPropertyAccessExpression(node.parent) && node.parent.name === node
    ? node.parent
    : node;
  const parent = expression.parent;
  return (ts.isCallExpression(parent) || ts.isNewExpression(parent)) && parent.expression === expression;
}

function objectLiteralHasRuntimeConsumer(objectLiteral) {
  let expression = objectLiteral;
  while (ts.isParenthesizedExpression(expression.parent) ||
         ts.isArrayLiteralExpression(expression.parent) ||
         ts.isObjectLiteralExpression(expression.parent)) {
    expression = expression.parent;
  }
  const parent = expression.parent;
  if ((ts.isCallExpression(parent) || ts.isNewExpression(parent)) && parent.arguments.includes(expression)) {
    return true;
  }
  if (ts.isVariableDeclaration(parent) && parent.initializer === expression && ts.isIdentifier(parent.name)) {
    const symbol = resolveSymbol(checker.getSymbolAtLocation(parent.name));
    if (!symbol) {
      return false;
    }
    for (const sourceFile of programSources) {
      let found = false;
      visit(sourceFile, (node) => {
        if (!found && node !== parent.name && ts.isIdentifier(node) &&
            symbolsEquivalent(checker.getSymbolAtLocation(node), symbol) &&
            !isTypeOnlyReference(node) &&
            !isWriteOnlyReference(node)) {
          found = true;
        }
      });
      if (found) {
        return true;
      }
    }
  }
  return false;
}

function collectTemplateIdentifiers(template, counts) {
  const root = parseVueTemplate(template);
  walkVue(root);

  function walkVue(node) {
    if (node.type === VueNodeTypes.SIMPLE_EXPRESSION && !node.isStatic) {
      const source = ts.createSourceFile(
        "<vue-template-expression>.ts",
        `(${node.content})`,
        ts.ScriptTarget.ESNext,
        true,
        ts.ScriptKind.TS);
      visit(source, (typescriptNode) => {
        if (ts.isIdentifier(typescriptNode)) {
          counts.set(typescriptNode.text, (counts.get(typescriptNode.text) ?? 0) + 1);
        }
      });
    }
    for (const key of ["children", "props", "branches"]) {
      const children = node[key];
      if (Array.isArray(children)) {
        children.forEach(walkVue);
      }
    }
    for (const key of ["content", "exp", "arg", "condition"]) {
      const child = node[key];
      if (child && typeof child === "object" && typeof child.type === "number") {
        walkVue(child);
      }
    }
  }
}
