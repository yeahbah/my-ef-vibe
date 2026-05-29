using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace MyEfVibe;

/// <summary>
/// Discovers field, property, and parameter names typed as the selected (or other) DbContext types in a source file.
/// </summary>
internal sealed class DbContextInstanceIdentifierIndex
{
    private readonly HashSet<string> _selectedIdentifiers;
    private readonly HashSet<string> _otherIdentifiers;

    private DbContextInstanceIdentifierIndex(
        HashSet<string> selectedIdentifiers,
        HashSet<string> otherIdentifiers)
    {
        _selectedIdentifiers = selectedIdentifiers;
        _otherIdentifiers = otherIdentifiers;
    }

    internal IReadOnlySet<string> SelectedContextIdentifiers => _selectedIdentifiers;

    internal IReadOnlySet<string> OtherContextIdentifiers => _otherIdentifiers;

    internal static DbContextInstanceIdentifierIndex Empty { get; } =
        new(new HashSet<string>(StringComparer.Ordinal), new HashSet<string>(StringComparer.Ordinal));

    internal static DbContextInstanceIdentifierIndex Build(string sourceText, DbContextScanScope scope)
    {
        var selected = new HashSet<string>(StringComparer.Ordinal);
        var other = new HashSet<string>(StringComparer.Ordinal);

        SyntaxTree tree;

        try
        {
            tree = CSharpSyntaxTree.ParseText(sourceText);
        }
        catch (Exception)
        {
            return new DbContextInstanceIdentifierIndex(selected, other);
        }

        var root = tree.GetCompilationUnitRoot();

        foreach (var typeDeclaration in root.DescendantNodes().OfType<TypeDeclarationSyntax>())
        {
            foreach (var parameter in typeDeclaration.ParameterList?.Parameters ?? [])
                AddIdentifierForType(parameter.Type, parameter.Identifier.Text, scope, selected, other);

            foreach (var member in typeDeclaration.Members)
            {
                switch (member)
                {
                    case FieldDeclarationSyntax field:
                        foreach (var variable in field.Declaration.Variables)
                            AddIdentifierForType(field.Declaration.Type, variable.Identifier.Text, scope, selected, other);

                        break;

                    case PropertyDeclarationSyntax property:
                        AddIdentifierForType(property.Type, property.Identifier.Text, scope, selected, other);
                        break;
                }
            }
        }

        foreach (var method in root.DescendantNodes().OfType<BaseMethodDeclarationSyntax>())
        {
            foreach (var parameter in method.ParameterList?.Parameters ?? [])
                AddIdentifierForType(parameter.Type, parameter.Identifier.Text, scope, selected, other);
        }

        return new DbContextInstanceIdentifierIndex(selected, other);
    }

    internal bool StatementReferencesSelectedContextInstance(string statement)
    {
        foreach (var identifier in _selectedIdentifiers)
        {
            if (StatementContainsInstanceAccess(statement, identifier))
                return true;
        }

        return false;
    }

    internal bool StatementReferencesOtherContextInstance(string statement)
    {
        foreach (var identifier in _otherIdentifiers)
        {
            if (StatementContainsInstanceAccess(statement, identifier))
                return true;
        }

        return false;
    }

    internal IEnumerable<string> EnumerateSelectedMemberPrefixes()
    {
        yield return "db.";

        foreach (var identifier in _selectedIdentifiers)
            yield return $"{identifier}.";
    }

    private static void AddIdentifierForType(
        TypeSyntax? typeSyntax,
        string identifier,
        DbContextScanScope scope,
        HashSet<string> selected,
        HashSet<string> other)
    {
        if (string.IsNullOrWhiteSpace(identifier))
            return;

        if (!DbContextTypeNameSyntax.TryGetSimpleTypeName(typeSyntax, out var typeName))
            return;

        switch (DbContextTypeNameSyntax.Classify(typeName, scope))
        {
            case DbContextTypeClassification.Selected:
                selected.Add(identifier);
                break;

            case DbContextTypeClassification.Other:
                other.Add(identifier);
                break;
        }
    }

    private static bool StatementContainsInstanceAccess(string statement, string identifier) =>
        statement.Contains($"{identifier}.", StringComparison.Ordinal)
        || statement.Contains($"this.{identifier}.", StringComparison.Ordinal);
}
