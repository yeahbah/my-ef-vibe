using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace MyEfVibe;

/// <summary>
///     Replaces method-parameter identifiers in deep-scan probes with literals so expressions compile in the REPL.
/// </summary>
internal static class ProbeParameterStubber
{
    internal static string Stub(string probeExpression, ProbeStubContext? context = null)
    {
        if (string.IsNullOrWhiteSpace(probeExpression))
        {
            return probeExpression;
        }

        try
        {
            var singleLine = ProbeScriptFormatter.ToScriptExpression(probeExpression);
            var wrapped = $"var __efProbe = {singleLine};";

            var tree = CSharpSyntaxTree.ParseText(
                wrapped,
                CSharpParseOptions.Default.WithKind(SourceCodeKind.Script));

            var rewritten = new ParameterStubRewriter(context).Visit(tree.GetRoot());

            if (rewritten is null)
            {
                return singleLine;
            }

            var text = rewritten.ToFullString();

            return UnwrapProbeAssignment(text) ?? singleLine;
        }
        catch (Exception)
        {
            return ProbeScriptFormatter.ToScriptExpression(probeExpression);
        }
    }

    private static string? UnwrapProbeAssignment(string rewritten)
    {
        const string prefix = "var __efProbe = ";

        var index = rewritten.IndexOf(prefix, StringComparison.Ordinal);

        if (index < 0)
        {
            return rewritten.Trim();
        }

        var start = index + prefix.Length;
        var end = rewritten.LastIndexOf(';');

        if (end <= start)
        {
            return rewritten[start..].Trim();
        }

        return rewritten[start..end].Trim();
    }

    private sealed class ParameterStubRewriter : CSharpSyntaxRewriter
    {
        private readonly ProbeStubContext? _context;
        private readonly Stack<HashSet<string>> _lambdaParameters = new();

        internal ParameterStubRewriter(ProbeStubContext? context)
        {
            _context = context;
        }

        public override SyntaxNode? VisitSimpleLambdaExpression(SimpleLambdaExpressionSyntax node)
        {
            _lambdaParameters.Push(new HashSet<string>(StringComparer.Ordinal)
            {
                node.Parameter.Identifier.Text
            });

            var rewritten = base.VisitSimpleLambdaExpression(node);
            _lambdaParameters.Pop();

            return rewritten;
        }

        public override SyntaxNode? VisitParenthesizedLambdaExpression(ParenthesizedLambdaExpressionSyntax node)
        {
            var names = node.ParameterList.Parameters
                .Select(static parameter => parameter.Identifier.Text)
                .ToHashSet(StringComparer.Ordinal);

            _lambdaParameters.Push(names);

            var rewritten = base.VisitParenthesizedLambdaExpression(node);
            _lambdaParameters.Pop();

            return rewritten;
        }

        public override SyntaxNode? VisitMemberAccessExpression(MemberAccessExpressionSyntax node)
        {
            if (TryCreateStubFromOuterMemberAccess(node, out var literal))
            {
                return literal;
            }

            return base.VisitMemberAccessExpression(node);
        }

        public override SyntaxNode? VisitInvocationExpression(InvocationExpressionSyntax node)
        {
            var rewritten = (InvocationExpressionSyntax)base.VisitInvocationExpression(node)!;

            if (rewritten.ArgumentList is null)
            {
                return rewritten;
            }

            var arguments = rewritten.ArgumentList.Arguments;

            if (arguments.Count == 0)
            {
                return rewritten;
            }

            var filtered = arguments
                .Where(static argument => !IsCancellationTokenArgument(argument))
                .ToArray();

            if (filtered.Length == arguments.Count)
            {
                return rewritten;
            }

            return rewritten.WithArgumentList(
                SyntaxFactory.ArgumentList(SyntaxFactory.SeparatedList(filtered)));
        }

        public override SyntaxNode? VisitIdentifierName(IdentifierNameSyntax node)
        {
            var name = node.Identifier.Text;

            if (string.Equals(name, "db", StringComparison.Ordinal)
                || string.Equals(name, "cancellationToken", StringComparison.OrdinalIgnoreCase)
                || IsLambdaParameter(name)
                || IsDeclaredInProbe(node)
                || IsInTypeContext(node)
                || IsMemberAccessPart(node)
                || IsAnonymousTypeMemberName(node)
                || IsObjectInitializerMemberName(node)
                || IsNamedArgumentName(node))
            {
                return base.VisitIdentifierName(node);
            }

            if (IsCollectionContainsReceiver(node))
            {
                return CreateCollectionStubLiteral(name);
            }

            if (node.Parent is BinaryExpressionSyntax binary)
            {
                return CreateStubLiteralForComparison(name, binary, node);
            }

            return CreateStubLiteral(name);
        }

        private static bool IsCollectionContainsReceiver(IdentifierNameSyntax node)
        {
            return node.Parent is MemberAccessExpressionSyntax
                   {
                       Expression: var receiver,
                       Name.Identifier.Text: "Contains"
                   }
                   && receiver == node;
        }

        private ExpressionSyntax CreateStubLiteralForComparison(
            string name,
            BinaryExpressionSyntax binary,
            IdentifierNameSyntax node)
        {
            var other = binary.Left == node ? binary.Right : binary.Left;

            if (TryCreateStubLiteralFromEntityProperty(other, out var entityPropertyLiteral))
            {
                return entityPropertyLiteral;
            }

            if (other is MemberAccessExpressionSyntax outerMemberAccess
                && TryCreateStubFromOuterMemberAccess(outerMemberAccess, out var outerMemberLiteral))
            {
                return outerMemberLiteral;
            }

            if (IsComparedToGuidMember(other))
            {
                return GuidStubLiteral();
            }

            if (IsComparedToNumericMember(other))
            {
                return NumericStubLiteral();
            }

            if (IsComparedToStringMember(other))
            {
                return SyntaxFactory.LiteralExpression(
                    SyntaxKind.StringLiteralExpression,
                    SyntaxFactory.Literal(string.Empty));
            }

            return CreateStubLiteral(name);
        }

        private bool TryCreateStubLiteralFromEntityProperty(
            ExpressionSyntax other,
            out ExpressionSyntax literal)
        {
            literal = null!;

            if (_context?.DbContextType is not { } dbContextType
                || string.IsNullOrWhiteSpace(_context.EntityTypeName)
                || !TryGetEntityMemberName(other, out var memberName)
                || !EntityPropertyTypeResolver.TryGetPropertyType(
                    dbContextType,
                    _context.EntityTypeName,
                    memberName,
                    out var propertyType)
                || propertyType is null)
            {
                return false;
            }

            literal = CreateStubLiteralForClrType(propertyType);

            return true;
        }

        private bool TryCreateStubFromOuterMemberAccess(
            MemberAccessExpressionSyntax memberAccess,
            out ExpressionSyntax literal)
        {
            literal = null!;

            if (memberAccess.Expression is not IdentifierNameSyntax receiver)
            {
                return false;
            }

            var receiverName = receiver.Identifier.Text;

            if (string.Equals(receiverName, "db", StringComparison.Ordinal)
                || IsLambdaParameter(receiverName)
                || IsDeclaredInProbe(receiver))
            {
                return false;
            }

            if (_context?.DbContextType is not { } dbContextType)
            {
                return false;
            }

            var memberName = memberAccess.Name.Identifier.Text;

            if (!OuterEntityMemberResolver.TryResolvePropertyType(
                    dbContextType,
                    receiverName,
                    memberName,
                    out var propertyType)
                || propertyType is null)
            {
                return false;
            }

            literal = CreateStubLiteralForClrType(propertyType);

            return true;
        }

        private static bool TryGetEntityMemberName(ExpressionSyntax other, out string memberName)
        {
            memberName = string.Empty;

            if (other is not MemberAccessExpressionSyntax memberAccess)
            {
                return false;
            }

            memberName = memberAccess.Name.Identifier.Text;

            return !string.IsNullOrWhiteSpace(memberName);
        }

        private static ExpressionSyntax CreateStubLiteralForClrType(Type propertyType)
        {
            var underlying = Nullable.GetUnderlyingType(propertyType) ?? propertyType;

            if (underlying == typeof(Guid))
            {
                return GuidStubLiteral();
            }

            if (underlying == typeof(string))
            {
                return SyntaxFactory.LiteralExpression(
                    SyntaxKind.StringLiteralExpression,
                    SyntaxFactory.Literal(string.Empty));
            }

            if (underlying == typeof(bool))
            {
                return SyntaxFactory.LiteralExpression(SyntaxKind.TrueLiteralExpression);
            }

            if (underlying == typeof(DateTime) || underlying == typeof(DateTimeOffset))
            {
                return SyntaxFactory.ParseExpression("DateTime.UtcNow");
            }

            if (IsIntegralNumericType(underlying) || underlying == typeof(decimal) || underlying == typeof(double) ||
                underlying == typeof(float))
            {
                return NumericStubLiteral();
            }

            return NumericStubLiteral();
        }

        private static bool IsIntegralNumericType(Type type)
        {
            return type == typeof(int)
                   || type == typeof(long)
                   || type == typeof(short)
                   || type == typeof(byte)
                   || type == typeof(uint)
                   || type == typeof(ulong)
                   || type == typeof(ushort)
                   || type == typeof(sbyte);
        }

        private static bool IsComparedToGuidMember(ExpressionSyntax other)
        {
            return other switch
            {
                MemberAccessExpressionSyntax memberAccess =>
                    IsGuidMemberName(memberAccess.Name.Identifier.Text),

                IdentifierNameSyntax identifier =>
                    IsGuidMemberName(identifier.Identifier.Text),

                _ => false
            };
        }

        private static bool IsComparedToNumericMember(ExpressionSyntax other)
        {
            return other switch
            {
                MemberAccessExpressionSyntax memberAccess =>
                    IsNumericMemberName(memberAccess.Name.Identifier.Text),

                IdentifierNameSyntax identifier =>
                    IsNumericMemberName(identifier.Identifier.Text),

                _ => false
            };
        }

        private static bool IsGuidMemberName(string member)
        {
            return string.Equals(member, "Rowguid", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(member, "Guid", StringComparison.OrdinalIgnoreCase)
                   || member.EndsWith("Guid", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsNumericMemberName(string member)
        {
            if (IsGuidMemberName(member))
            {
                return false;
            }

            return (member.EndsWith("Id", StringComparison.OrdinalIgnoreCase)
                    && !member.EndsWith("ObjectId", StringComparison.OrdinalIgnoreCase))
                   || member.EndsWith("Count", StringComparison.OrdinalIgnoreCase)
                   || member.EndsWith("Key", StringComparison.OrdinalIgnoreCase)
                   || member.EndsWith("No", StringComparison.OrdinalIgnoreCase)
                   || (member.EndsWith("Number", StringComparison.OrdinalIgnoreCase)
                       && !member.EndsWith("Name", StringComparison.OrdinalIgnoreCase));
        }

        private static bool IsComparedToStringMember(ExpressionSyntax other)
        {
            if (other is not MemberAccessExpressionSyntax memberAccess)
            {
                return false;
            }

            var member = memberAccess.Name.Identifier.Text;

            return member.EndsWith("Code", StringComparison.OrdinalIgnoreCase)
                   || member.EndsWith("Name", StringComparison.OrdinalIgnoreCase)
                   || member.EndsWith("Email", StringComparison.OrdinalIgnoreCase)
                   || member.EndsWith("Title", StringComparison.OrdinalIgnoreCase)
                   || (member.EndsWith("Number", StringComparison.OrdinalIgnoreCase)
                       && !member.EndsWith("Id", StringComparison.OrdinalIgnoreCase));
        }

        private static LiteralExpressionSyntax NumericStubLiteral()
        {
            return SyntaxFactory.LiteralExpression(SyntaxKind.NumericLiteralExpression, SyntaxFactory.Literal(0));
        }

        private static ExpressionSyntax GuidStubLiteral()
        {
            return SyntaxFactory.ParseExpression("Guid.Empty");
        }

        private static bool IsInTypeContext(IdentifierNameSyntax node)
        {
            for (var parent = node.Parent; parent is not null; parent = parent.Parent)
            {
                switch (parent)
                {
                    case TypeArgumentListSyntax:
                    case ArrayTypeSyntax:
                    case NullableTypeSyntax:
                    case TupleTypeSyntax:
                        return true;

                    case VariableDeclarationSyntax variableDeclaration when variableDeclaration.Type == node:
                    case ParameterSyntax parameter when parameter.Type == node:
                    case CastExpressionSyntax cast when cast.Type == node:
                    case TypeOfExpressionSyntax typeOf when typeOf.Type == node:
                    case DefaultExpressionSyntax defaultExpression when defaultExpression.Type == node:
                    case ObjectCreationExpressionSyntax objectCreation when objectCreation.Type == node:
                        return true;

                    case QualifiedNameSyntax qualified when qualified.Right == node:
                        return true;
                }
            }

            return false;
        }

        private static bool IsMemberAccessPart(IdentifierNameSyntax node)
        {
            return node.Parent is MemberAccessExpressionSyntax memberAccess
                   && memberAccess.Name == node;
        }

        private static bool IsAnonymousTypeMemberName(IdentifierNameSyntax node)
        {
            return node.Parent is NameEqualsSyntax nameEquals && nameEquals.Name == node;
        }

        private static bool IsNamedArgumentName(IdentifierNameSyntax node)
        {
            return node.Parent is NameColonSyntax nameColon && nameColon.Name == node;
        }

        private static bool IsObjectInitializerMemberName(IdentifierNameSyntax node)
        {
            return node.Parent is AssignmentExpressionSyntax { Left: var left } assignment
                   && left == node
                   && assignment.Parent is InitializerExpressionSyntax;
        }

        private static bool IsCancellationTokenArgument(ArgumentSyntax argument)
        {
            if (argument.NameColon?.Name.Identifier.Text is "cancellationToken")
            {
                return true;
            }

            return argument.Expression switch
            {
                IdentifierNameSyntax { Identifier.Text: "cancellationToken" } => true,
                _ => false
            };
        }

        private bool IsLambdaParameter(string name)
        {
            foreach (var scope in _lambdaParameters)
            {
                if (scope.Contains(name))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsDeclaredInProbe(IdentifierNameSyntax node)
        {
            for (var current = node.Parent; current is not null; current = current.Parent)
            {
                if (current is VariableDeclaratorSyntax declarator
                    && declarator.Identifier.Text == node.Identifier.Text)
                {
                    return true;
                }
            }

            return false;
        }

        private static ExpressionSyntax CreateStubLiteral(string name)
        {
            if (string.Equals(name, "code", StringComparison.OrdinalIgnoreCase))
            {
                return SyntaxFactory.LiteralExpression(
                    SyntaxKind.StringLiteralExpression,
                    SyntaxFactory.Literal(string.Empty));
            }

            if (IsLikelyCollectionParameterName(name))
            {
                return CreateCollectionStubLiteral(name);
            }

            if (IsLikelyGuidParameterName(name))
            {
                return GuidStubLiteral();
            }

            if (string.Equals(name, "key", StringComparison.OrdinalIgnoreCase)
                || string.Equals(name, "id", StringComparison.OrdinalIgnoreCase)
                || (name.EndsWith("Id", StringComparison.OrdinalIgnoreCase)
                    && name.Length > 2
                    && !name.EndsWith("ObjectId", StringComparison.OrdinalIgnoreCase))
                || name.EndsWith("Count", StringComparison.OrdinalIgnoreCase)
                || string.Equals(name, "count", StringComparison.OrdinalIgnoreCase))
            {
                return NumericStubLiteral();
            }

            if (IsLikelyBooleanParameterName(name))
            {
                return SyntaxFactory.LiteralExpression(SyntaxKind.TrueLiteralExpression);
            }

            if (name.Contains("Name", StringComparison.OrdinalIgnoreCase)
                || name.Contains("Email", StringComparison.OrdinalIgnoreCase)
                || name.Contains("Title", StringComparison.OrdinalIgnoreCase)
                || name.Contains("Description", StringComparison.OrdinalIgnoreCase)
                || name.Contains("Code", StringComparison.OrdinalIgnoreCase)
                || name.Contains("Term", StringComparison.OrdinalIgnoreCase))
            {
                return SyntaxFactory.LiteralExpression(
                    SyntaxKind.StringLiteralExpression,
                    SyntaxFactory.Literal(string.Empty));
            }

            if (name.Contains("Date", StringComparison.OrdinalIgnoreCase)
                || name.EndsWith("At", StringComparison.OrdinalIgnoreCase))
            {
                return SyntaxFactory.ParseExpression("DateTime.UtcNow");
            }

            return NumericStubLiteral();
        }

        private static bool IsLikelyBooleanParameterName(string name)
        {
            if (name.Length < 4)
            {
                return false;
            }

            if (name.StartsWith("is", StringComparison.OrdinalIgnoreCase)
                && char.IsUpper(name[2]))
            {
                return true;
            }

            if (name.StartsWith("has", StringComparison.OrdinalIgnoreCase)
                && name.Length > 3
                && char.IsUpper(name[3]))
            {
                return true;
            }

            if (name.StartsWith("can", StringComparison.OrdinalIgnoreCase)
                && name.Length > 3
                && char.IsUpper(name[3]))
            {
                return true;
            }

            return false;
        }

        private static bool IsLikelyGuidParameterName(string name)
        {
            return name.Contains("Guid", StringComparison.OrdinalIgnoreCase)
                   || name.EndsWith("ObjectId", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(name, "rowguid", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsLikelyCollectionParameterName(string name)
        {
            return name.EndsWith("Ids", StringComparison.OrdinalIgnoreCase)
                   || name.EndsWith("Keys", StringComparison.OrdinalIgnoreCase)
                   || name.EndsWith("Codes", StringComparison.OrdinalIgnoreCase)
                   || name.EndsWith("Values", StringComparison.OrdinalIgnoreCase)
                   || name.EndsWith("List", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(name, "ids", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(name, "keys", StringComparison.OrdinalIgnoreCase);
        }

        private static ExpressionSyntax CreateCollectionStubLiteral(string name)
        {
            if (name.Contains("Guid", StringComparison.OrdinalIgnoreCase))
            {
                return SyntaxFactory.ParseExpression("new[] { Guid.Empty }");
            }

            if (name.Contains("Code", StringComparison.OrdinalIgnoreCase)
                || name.Contains("Name", StringComparison.OrdinalIgnoreCase)
                || name.Contains("Email", StringComparison.OrdinalIgnoreCase)
                || name.Contains("Sku", StringComparison.OrdinalIgnoreCase))
            {
                return SyntaxFactory.ParseExpression("new string[] { \"\" }");
            }

            return SyntaxFactory.ParseExpression("new int[] { 0 }");
        }
    }
}