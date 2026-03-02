package com.unigrid.dataAccess.entities;

import jakarta.persistence.Column;
import jakarta.persistence.Embeddable;
import lombok.EqualsAndHashCode;
import lombok.Getter;
import lombok.Setter;

import java.io.Serializable;
import java.util.UUID;

@Getter
@Setter
@EqualsAndHashCode
@Embeddable
public class MemberWorkspaceId implements Serializable {
    private static final long serialVersionUID = 1743762183586476041L;
    @Column(name = "memberId", nullable = false)
    private UUID memberId;

    @Column(name = "workspaceId", nullable = false)
    private UUID workspaceId;


}