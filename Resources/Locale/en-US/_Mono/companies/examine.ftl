# Company examination text with gender-appropriate pronouns
examine-company = {GENDER($entity) ->
    [male] He is in the {$company} company.
    [female] She is in the {$company} company.
    *[other] They are in the {$company} company.
}
